#!/bin/sh
# om-prepare-test-env: generated entrypoint (contract v2)
# regenerate with: om-prepare-test-env --regenerate
# history:
#   2026-09-18 generated alongside test-env-up.sh — stops the app and drops the run's
#              throwaway database; never touches the repository's real database
#   2026-09-18 repair: also match the app by its absolute .ai/qa/run/.publish path — the up
#              script starts it through setsid, which can fork, so the recorded PID may
#              belong to the wrapper rather than the dotnet process. The pattern is scoped
#              to this repo's QA publish directory, so the sandbox's own app on port 3000
#              (started from a relative .publish path) is never matched.
set -eu

PROJECT_ROOT=$(cd "$(dirname "$0")/../.." && pwd)
QA_DIR="$PROJECT_ROOT/.ai/qa"
ENV_DESCRIPTOR="$QA_DIR/test-env.json"
LOCK_DIR="$QA_DIR/test-env.lock"
QA_ROOT="$QA_DIR/run"

log() { echo "· $*" >&2; }
json_get() { sed -n 's/.*"'"$1"'"[[:space:]]*:[[:space:]]*"\{0,1\}\([^",}]*\)"\{0,1\}.*/\1/p' "$2" 2>/dev/null | head -1; }

[ -f "$ENV_DESCRIPTOR" ] || { log "no descriptor at $ENV_DESCRIPTOR — nothing to stop"; exit 0; }

STARTED_BY_REPO=$(json_get startedByThisRepo "$ENV_DESCRIPTOR")
if [ "$STARTED_BY_REPO" != "true" ]; then
  log "this environment was not started by the repository — leaving it alone"
  exit 0
fi

APP_PID=$(json_get pid "$ENV_DESCRIPTOR")
if [ -n "$APP_PID" ] && kill -0 "$APP_PID" 2>/dev/null; then
  log "stopping the app (pid $APP_PID)"
  kill "$APP_PID" 2>/dev/null || true
  waited=0
  while kill -0 "$APP_PID" 2>/dev/null; do
    waited=$((waited + 1))
    [ "$waited" -gt 15 ] && { kill -9 "$APP_PID" 2>/dev/null || true; break; }
    sleep 1
  done
fi

# Belt and braces: the recorded PID may be the setsid wrapper. Match the dotnet process by the
# absolute publish path this repository's up script uses, and nothing else.
if command -v pgrep >/dev/null 2>&1; then
  for stray in $(pgrep -f "$QA_ROOT/.publish/" 2>/dev/null || true); do
    log "stopping a stray QA app process ($stray)"
    kill "$stray" 2>/dev/null || true
  done
fi

# Drop only the database this repository's up script created (scoped name landing_qa_*).
QA_DB=""
[ -f "$QA_DIR/test-env-db.name" ] && QA_DB=$(cat "$QA_DIR/test-env-db.name")
case "$QA_DB" in
  landing_qa_*)
    if [ -f "$PROJECT_ROOT/.env" ]; then
      # Subshell read — never export the repository's real credentials into this process.
      ADMIN_DB_URL=$(sh -c 'set -a; . "$1"; set +a; printf "%s" "${DATABASE_URL:-}"' _ "$PROJECT_ROOT/.env")
      if [ -n "$ADMIN_DB_URL" ]; then
        log "dropping the throwaway database $QA_DB"
        psql "$ADMIN_DB_URL" -qtAc "drop database if exists \"$QA_DB\" with (force)" >/dev/null 2>&1 \
          || psql "$ADMIN_DB_URL" -qtAc "drop database if exists \"$QA_DB\"" >/dev/null 2>&1 || true
      fi
    fi
    rm -f "$QA_DIR/test-env-db.name"
    ;;
  "") : ;;
  *) log "refusing to drop '$QA_DB' — not a landing_qa_* throwaway database" ;;
esac

rm -f "$QA_ROOT/.env"
rm -rf "$LOCK_DIR"

sed 's/"status": "running"/"status": "stopped"/' "$ENV_DESCRIPTOR" > "$ENV_DESCRIPTOR.tmp" \
  && mv "$ENV_DESCRIPTOR.tmp" "$ENV_DESCRIPTOR"

echo "TEST_ENV_STATUS=stopped"
