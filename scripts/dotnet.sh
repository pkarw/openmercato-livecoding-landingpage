#!/usr/bin/env bash
# Resolves the .NET SDK (installing it on first use) and forwards every argument to it.
# Keeps the sandbox, CI and a fresh clone on the same command: `scripts/dotnet.sh build`.
set -euo pipefail

CHANNEL="${DOTNET_CHANNEL:-LTS}"
LOCAL_ROOT="${DOTNET_INSTALL_DIR:-$HOME/.dotnet}"

find_dotnet() {
  if command -v dotnet >/dev/null 2>&1; then
    command -v dotnet
    return 0
  fi
  for candidate in "$LOCAL_ROOT/dotnet" /usr/share/dotnet/dotnet /usr/local/share/dotnet/dotnet; do
    [ -x "$candidate" ] && echo "$candidate" && return 0
  done
  return 1
}

if ! DOTNET_BIN="$(find_dotnet)"; then
  echo "· installing the .NET $CHANNEL SDK into $LOCAL_ROOT" >&2
  curl -sSL https://dot.net/v1/dotnet-install.sh -o /tmp/dotnet-install.sh
  bash /tmp/dotnet-install.sh --channel "$CHANNEL" --install-dir "$LOCAL_ROOT" --no-path >&2
  DOTNET_BIN="$LOCAL_ROOT/dotnet"
fi

export DOTNET_ROOT="$(dirname "$DOTNET_BIN")"
export PATH="$DOTNET_ROOT:$PATH"
export DOTNET_CLI_TELEMETRY_OPTOUT=1
export DOTNET_NOLOGO=1

exec "$DOTNET_BIN" "$@"
