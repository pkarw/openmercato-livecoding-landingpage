#!/usr/bin/env bash
# Builds the React app into server/wwwroot, then publishes the ASP.NET Core app to .publish/.
set -euo pipefail

root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$root"

echo "· building the client"
npm --prefix client install --no-audit --no-fund
npm --prefix client run build

echo "· publishing the server"
scripts/dotnet.sh publish server -c Release -o .publish

echo "✓ ready — run scripts/serve.sh"
