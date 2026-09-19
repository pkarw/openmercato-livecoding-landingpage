#!/usr/bin/env bash
# Regression check: the configured detector must reject a generated fake secret.
set -euo pipefail

gitleaks_bin="${GITLEAKS_BIN:-gitleaks}"
fixture_dir="$(mktemp -d)"
trap 'find "$fixture_dir" -type f -delete; rmdir "$fixture_dir"' EXIT

# Generate disposable key material so the fixture exercises a real detector rule
# without embedding a reusable credential in the repository.
openssl genpkey \
  -algorithm RSA \
  -pkeyopt rsa_keygen_bits:2048 \
  -out "$fixture_dir/fake-private-key.pem" \
  >/dev/null 2>&1

set +e
"$gitleaks_bin" dir --no-banner --redact "$fixture_dir" >/dev/null 2>&1
status=$?
set -e

if [ "$status" -ne 1 ]; then
  printf 'Expected Gitleaks to reject the synthetic fixture, got exit status %s.\n' "$status" >&2
  exit 1
fi

echo '✓ Gitleaks rejected the synthetic secret fixture'
