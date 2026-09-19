#!/usr/bin/env bash
# Serves the published app — API and landing page together on PORT (default 3000).
set -euo pipefail

root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$root"

# Both `db migrate` and the server itself need a live database, and the sandbox
# can resume with PostgreSQL wedged behind a stale lock file. No-op when it is
# already up. See scripts/postgres.sh.
bash scripts/postgres.sh

exec scripts/dotnet.sh .publish/openmercato-livecoding-landingpage.dll "$@"
