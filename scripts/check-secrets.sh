#!/usr/bin/env bash
# Scan the current tree and, when supplied, only the commits introduced by this change.
set -euo pipefail

gitleaks_bin="${GITLEAKS_BIN:-gitleaks}"
base_sha="${GITLEAKS_BASE_SHA:-}"
head_sha="${GITLEAKS_HEAD_SHA:-}"

"$gitleaks_bin" dir --no-banner --redact .

# Local runs may intentionally omit a range and scan only the current tree.
if [ -z "$base_sha" ] || [ -z "$head_sha" ]; then
  exit 0
fi

for sha in "$base_sha" "$head_sha"; do
  if [ "${#sha}" -ne 40 ] || printf '%s' "$sha" | grep -Eq '[^0-9a-fA-F]'; then
    echo 'Secret-scan commit bounds must be full Git object IDs.' >&2
    exit 2
  fi
done

# GitHub uses an all-zero before SHA when no prior commit exists. A full history
# scan would rediscover the known legacy incident, so the current-tree check is
# the safe fallback for that one event.
if [ "$base_sha" = '0000000000000000000000000000000000000000' ]; then
  exit 0
fi

git cat-file -e "${base_sha}^{commit}"
git cat-file -e "${head_sha}^{commit}"
"$gitleaks_bin" git --no-banner --redact --log-opts="${base_sha}..${head_sha}" .
