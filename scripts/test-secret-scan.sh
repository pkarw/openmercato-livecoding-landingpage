#!/usr/bin/env bash
# Regression check: the configured detector must reject a generated fake secret.
set -euo pipefail

gitleaks_bin="${GITLEAKS_BIN:-gitleaks}"
fixture_dir="$(mktemp -d)"
fixture_repo="$fixture_dir/repository"
script_root="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
trap 'find "$fixture_dir" -depth -delete' EXIT

git init --quiet "$fixture_repo"
git -C "$fixture_repo" config user.name 'Secret Scan Test'
git -C "$fixture_repo" config user.email 'secret-scan@example.invalid'
printf '%s\n' 'clean tree' > "$fixture_repo/README.md"
git -C "$fixture_repo" add README.md
git -C "$fixture_repo" commit --quiet -m 'test: establish clean base'
base_sha="$(git -C "$fixture_repo" rev-parse HEAD)"

# Generate disposable key material so the fixture exercises a real detector rule
# without embedding a reusable credential in the repository.
openssl genpkey \
  -algorithm RSA \
  -pkeyopt rsa_keygen_bits:2048 \
  -out "$fixture_repo/fake-private-key.pem" \
  >/dev/null 2>&1
git -C "$fixture_repo" add fake-private-key.pem
git -C "$fixture_repo" commit --quiet -m 'test: add synthetic secret'

find "$fixture_repo" -maxdepth 1 -name fake-private-key.pem -type f -delete
git -C "$fixture_repo" add --update
git -C "$fixture_repo" commit --quiet -m 'test: remove synthetic secret'
head_sha="$(git -C "$fixture_repo" rev-parse HEAD)"

# Prove the final files are clean before asking the range scan to find the
# deleted credential in Git history.
"$gitleaks_bin" dir --no-banner --redact "$fixture_repo" >/dev/null

set +e
(
  cd "$fixture_repo"
  GITLEAKS_BIN="$gitleaks_bin" \
    GITLEAKS_BASE_SHA="$base_sha" \
    GITLEAKS_HEAD_SHA="$head_sha" \
    "$script_root/check-secrets.sh"
) >/dev/null 2>&1
status=$?
set -e

if [ "$status" -ne 1 ]; then
  printf 'Expected Gitleaks to reject the synthetic fixture, got exit status %s.\n' "$status" >&2
  exit 1
fi

echo '✓ Gitleaks rejected a synthetic secret removed from the final tree'
