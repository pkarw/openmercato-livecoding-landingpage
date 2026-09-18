#!/usr/bin/env bash
# Serves the published app — API and landing page together on PORT (default 3000).
set -euo pipefail

root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$root"

exec scripts/dotnet.sh .publish/openmercato-livecoding-landingpage.dll "$@"
