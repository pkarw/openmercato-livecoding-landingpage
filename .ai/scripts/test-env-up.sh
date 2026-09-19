#!/bin/sh
# om-prepare-test-env: generated entrypoint (contract v2)
# regenerate with: om-prepare-test-env --regenerate
# history:
#   2026-09-18 generated for the -10% landing page (ASP.NET Core 10 + Vite React, PostgreSQL)
#   2026-09-18 isolate the app root under .ai/qa/run so the repository's tracked .env
#              (real DATABASE_URL and RESEND_API_KEY) is never loaded by a test run —
#              prevents QA claims from writing to real lead data or sending real email
#   2026-09-18 provision chromium's missing system libraries into ~/.local/pwlibs and
#              export LD_LIBRARY_PATH — the sandbox has no root, so `playwright
#              install-deps` cannot run and chromium dies with "libnspr4.so: cannot open"
#   2026-09-18 repair: read the repository .env in a subshell and launch the app through
#              `env -u` — `set -a; . .env` exported the REAL DATABASE_URL, RESEND_API_KEY
#              and PORT into the child, and a real environment variable beats the QA
#              .env file (DotEnv.Load), so the app bound the taken port 3000 and pointed
#              at the production database instead of the throwaway one
#   2026-09-18 repair: start the app with setsid — without it the app died with the invoking
#              shell and the next probe got ECONNREFUSED
set -eu

# ---------------------------------------------------------------- parameters
PROJECT_ROOT=$(cd "$(dirname "$0")/../.." && pwd)
QA_DIR="$PROJECT_ROOT/.ai/qa"
ENV_DESCRIPTOR="$QA_DIR/test-env.json"
BUILD_CACHE="$QA_DIR/test-env-build-cache.json"
CREDENTIALS_FILE="$QA_DIR/test-env.env"
LOCK_DIR="$QA_DIR/test-env.lock"

# The app resolves its repository root by walking up for openmercato.toml and then loads
# .env.local/.env from there. QA_ROOT is a throwaway root carrying ONLY disposable values,
# so a test run can never pick up the repository's real database or Resend credentials.
QA_ROOT="$QA_DIR/run"
APP_DLL_NAME="openmercato-livecoding-landingpage.dll"
APP_DLL="$QA_ROOT/.publish/$APP_DLL_NAME"

PREFERRED_PORT=${TEST_ENV_PORT:-4310}
HEALTH_PATH="/api/health"
CACHE_TTL_SECONDS=${TEST_ENV_CACHE_TTL_SECONDS:-600}
HEALTH_TIMEOUT=${TEST_ENV_HEALTH_TIMEOUT:-120}

DOTNET="$PROJECT_ROOT/scripts/dotnet.sh"
# Chromium system libraries unpacked without root (see history).
PW_LIBS="$HOME/.local/pwlibs"
PW_LD_PATH="$PW_LIBS/usr/lib/x86_64-linux-gnu:$PW_LIBS/lib/x86_64-linux-gnu"
PW_NODE_PATH=${TEST_ENV_PLAYWRIGHT_PREFIX:-/tmp/pwinst}

# Build inputs decide whether the publish chain can be skipped.
BUILD_INPUT_PATHS="client/package.json client/package-lock.json client/index.html client/vite.config.ts client/src server db/migrations"

FORCE=0
FORCE_REBUILD=0
for arg in "$@"; do
  case "$arg" in
    --force) FORCE=1 ;;
    --force-rebuild) FORCE_REBUILD=1 ;;
    *) echo "unknown flag: $arg" >&2; exit 2 ;;
  esac
done

mkdir -p "$QA_DIR"

log() { echo "· $*" >&2; }

free_port() {
  if command -v python3 >/dev/null 2>&1; then
    python3 -c 'import socket;s=socket.socket();s.bind(("127.0.0.1",0));print(s.getsockname()[1]);s.close()'
  elif command -v node >/dev/null 2>&1; then
    node -e 's=require("net").createServer();s.listen(0,"127.0.0.1",()=>{console.log(s.address().port);s.close()})'
  else
    awk 'BEGIN{srand();print 20000+int(rand()*20000)}'
  fi
}

port_free() {
  if command -v node >/dev/null 2>&1; then
    node -e 'const n=require("net").createServer();n.once("error",()=>process.exit(1));n.listen(Number(process.argv[1]),"127.0.0.1",()=>{n.close(()=>process.exit(0))})' "$1"
  else
    return 1
  fi
}

# ---------------------------------------------------------------- 2. lock
RELEASE_LOCK=0
cleanup() {
  [ "$RELEASE_LOCK" = 1 ] && rm -rf "$LOCK_DIR"
  return 0
}
trap cleanup EXIT INT TERM

acquire_lock() {
  waited=0
  while :; do
    if mkdir "$LOCK_DIR" 2>/dev/null; then
      printf '{"pid":%s,"source":"test-env-up.sh","acquiredAt":"%s"}\n' \
        "$$" "$(date -u +%FT%TZ)" > "$LOCK_DIR/owner.json"
      RELEASE_LOCK=1
      return 0
    fi
    owner=$(sed -n 's/.*"pid":\([0-9]*\).*/\1/p' "$LOCK_DIR/owner.json" 2>/dev/null || true)
    if [ -z "$owner" ] || ! kill -0 "$owner" 2>/dev/null; then
      log "stale bootstrap lock (owner ${owner:-unknown} gone) — reclaiming"
      rm -rf "$LOCK_DIR"
      continue
    fi
    waited=$((waited + 3))
    if [ "$waited" -gt 300 ]; then
      log "another bootstrap (pid $owner) held the lock for 5 minutes — giving up"
      exit 1
    fi
    sleep 3
  done
}
acquire_lock

# ---------------------------------------------------------------- 3. reuse check
json_get() { sed -n 's/.*"'"$1"'"[[:space:]]*:[[:space:]]*"\{0,1\}\([^",}]*\)"\{0,1\}.*/\1/p' "$2" 2>/dev/null | head -1; }

REUSED=0
if [ "$FORCE" = 0 ] && [ -f "$ENV_DESCRIPTOR" ]; then
  d_status=$(json_get status "$ENV_DESCRIPTOR")
  d_pid=$(json_get pid "$ENV_DESCRIPTOR")
  d_url=$(json_get baseUrl "$ENV_DESCRIPTOR")
  d_started=$(json_get startedAt "$ENV_DESCRIPTOR")
  if [ "$d_status" = "running" ] && [ -n "$d_pid" ] && kill -0 "$d_pid" 2>/dev/null &&
     curl -fsS -m 5 "$d_url$HEALTH_PATH" >/dev/null 2>&1 &&
     curl -fsS -m 5 "$d_url/api/offer" >/dev/null 2>&1; then
    age=$(( $(date -u +%s) - $(date -u -d "$d_started" +%s 2>/dev/null || echo 0) ))
    newer=$(cd "$PROJECT_ROOT" && find $BUILD_INPUT_PATHS -newermt "$d_started" -type f 2>/dev/null | head -1)
    if [ "$age" -lt "$CACHE_TTL_SECONDS" ] && [ -z "$newer" ]; then
      log "reusing the running environment at $d_url"
      REUSED=1
    else
      log "running environment is stale (age ${age}s, changed: ${newer:-none}) — restarting"
    fi
  fi
fi

if [ "$REUSED" = 1 ]; then
  echo "TEST_ENV_STATUS=running"
  echo "TEST_ENV_BASE_URL=$d_url"
  echo "TEST_ENV_DESCRIPTOR=.ai/qa/test-env.json"
  echo "TEST_ENV_REUSED=1"
  echo "BROWSER_PROVIDER=playwright"
  echo "BROWSER_INSTALLED=1"
  exit 0
fi

# A previous instance that failed a probe must not keep the port.
if [ -f "$ENV_DESCRIPTOR" ]; then
  old_pid=$(json_get pid "$ENV_DESCRIPTOR")
  if [ -n "$old_pid" ] && kill -0 "$old_pid" 2>/dev/null; then
    log "stopping the previous app (pid $old_pid)"
    kill "$old_pid" 2>/dev/null || true
    sleep 2
  fi
fi

# ---------------------------------------------------------------- 4. build cache
fingerprint() {
  (cd "$PROJECT_ROOT" && find $BUILD_INPUT_PATHS -type f \
      ! -path '*/node_modules/*' ! -path '*/bin/*' ! -path '*/obj/*' \
      -exec stat -c '%n:%s:%Y' {} + 2>/dev/null | sort | cksum) | tr -d ' \n'
}

FP=$(fingerprint)
CACHED_FP=$(json_get fingerprint "$BUILD_CACHE")
if [ "$FORCE_REBUILD" = 0 ] && [ "$CACHED_FP" = "$FP" ] && [ -f "$APP_DLL" ]; then
  log "build cache hit — skipping npm build and dotnet publish"
else
  log "building the client"
  if [ ! -d "$PROJECT_ROOT/client/node_modules" ]; then
    npm --prefix "$PROJECT_ROOT/client" ci --no-audit --no-fund >&2
  fi
  npm --prefix "$PROJECT_ROOT/client" run build >&2

  log "publishing the server into $QA_ROOT/.publish"
  rm -rf "$QA_ROOT/.publish"
  "$DOTNET" publish "$PROJECT_ROOT/server" -c Release -o "$QA_ROOT/.publish" >&2

  printf '{"fingerprint":"%s","root":"%s","builtAt":"%s"}\n' \
    "$FP" "$PROJECT_ROOT" "$(date -u +%FT%TZ)" > "$BUILD_CACHE"
fi

# ---------------------------------------------------------------- 5. services + isolated app root
# The repository's PostgreSQL server is reused (no Docker in this sandbox), but every run gets
# its OWN throwaway database, so real lead rows are never read or written by QA.
[ -f "$PROJECT_ROOT/.env" ] || { echo "missing $PROJECT_ROOT/.env — cannot reach PostgreSQL" >&2; exit 1; }
# Read it in a SUBSHELL: sourcing it here would export the repository's real DATABASE_URL,
# RESEND_API_KEY and PORT into every child process, and a real environment variable beats the
# QA .env file the app reads (Landing.Configuration.DotEnv).
ADMIN_DB_URL=$(sh -c 'set -a; . "$1"; set +a; printf "%s" "${DATABASE_URL:-}"' _ "$PROJECT_ROOT/.env")
[ -n "$ADMIN_DB_URL" ] || { echo "DATABASE_URL is not set in $PROJECT_ROOT/.env" >&2; exit 1; }

# Every app invocation runs with the repository's configuration variables cleared, so only
# .ai/qa/run/.env can supply them.
APP_ENV="env -u DATABASE_URL -u REDIS_URL -u PORT -u RESEND_API_KEY -u ADMIN_EMAIL -u LEADS_INBOX -u CACHE_TTL_SECONDS -u OFFER_DISCOUNT_PERCENT -u OFFER_ENDS_AT"

RUN_ID="$(date -u +%Y%m%d%H%M%S)$$"
QA_DB="landing_qa_$RUN_ID"
QA_DB_URL=$(printf '%s' "$ADMIN_DB_URL" | sed -E "s#(://[^/]+)/[^?]*#\1/$QA_DB#")

log "creating the throwaway database $QA_DB"
psql "$ADMIN_DB_URL" -v ON_ERROR_STOP=1 -qtAc "create database \"$QA_DB\"" >/dev/null

# Throwaway app root: openmercato.toml marks it, .env carries disposable values only.
mkdir -p "$QA_ROOT/db"
cp "$PROJECT_ROOT/openmercato.toml" "$QA_ROOT/openmercato.toml"
rm -rf "$QA_ROOT/db/migrations"
cp -R "$PROJECT_ROOT/db/migrations" "$QA_ROOT/db/migrations"

PORT_TO_USE="$PREFERRED_PORT"
port_free "$PORT_TO_USE" || PORT_TO_USE=$(free_port)

# No RESEND_API_KEY here on purpose: EmailSettings.IsConfigured stays false and the app logs
# "skipping email" instead of calling Resend. No REDIS_URL either — the cache is best-effort and
# the shared sandbox Redis holds the real app's lead-count key.
cat > "$QA_ROOT/.env" <<EOF
# om-prepare-test-env: disposable QA values only. Never add real credentials here.
DATABASE_URL=$QA_DB_URL
PORT=$PORT_TO_USE
OFFER_DISCOUNT_PERCENT=15
OFFER_ENDS_AT=2026-09-20T23:59:59+02:00
EOF
chmod 600 "$QA_ROOT/.env"

log "migrating $QA_DB"
$APP_ENV "$DOTNET" "$APP_DLL" db migrate >&2

# ---------------------------------------------------------------- 6. app start + health wait
LOG_FILE="$QA_DIR/test-env-app.log"
: > "$LOG_FILE"
# setsid detaches the app from this script's process group, so it survives the caller's shell
# being torn down between commands (without it the environment dies the moment the invoking
# tool call returns and the next probe gets ECONNREFUSED).
if command -v setsid >/dev/null 2>&1; then
  setsid $APP_ENV "$DOTNET" "$APP_DLL" >>"$LOG_FILE" 2>&1 &
else
  nohup $APP_ENV "$DOTNET" "$APP_DLL" >>"$LOG_FILE" 2>&1 &
fi
APP_PID=$!
BASE_URL="http://127.0.0.1:$PORT_TO_USE"

waited=0
until curl -fsS -m 3 "$BASE_URL$HEALTH_PATH" >/dev/null 2>&1; do
  if ! kill -0 "$APP_PID" 2>/dev/null; then
    echo "the app exited during startup — last log lines:" >&2
    tail -20 "$LOG_FILE" >&2
    psql "$ADMIN_DB_URL" -qtAc "drop database if exists \"$QA_DB\"" >/dev/null 2>&1 || true
    exit 1
  fi
  waited=$((waited + 2))
  [ "$waited" -gt "$HEALTH_TIMEOUT" ] && { echo "health check timed out after ${HEALTH_TIMEOUT}s" >&2; tail -20 "$LOG_FILE" >&2; exit 1; }
  sleep 2
done

# ---------------------------------------------------------------- 7. descriptor + output
: > "$CREDENTIALS_FILE"
chmod 600 "$CREDENTIALS_FILE"

cat > "$ENV_DESCRIPTOR" <<EOF
{
  "version": 1,
  "runId": "$RUN_ID",
  "status": "running",
  "mode": "ephemeral",
  "baseUrl": "$BASE_URL",
  "startedByThisRepo": true,
  "startScript": ".ai/scripts/test-env-up.sh",
  "stopScript": ".ai/scripts/test-env-down.sh",
  "app": {
    "startCommand": "scripts/dotnet.sh .ai/qa/run/.publish/$APP_DLL_NAME",
    "port": $PORT_TO_USE,
    "healthPath": "$HEALTH_PATH",
    "pid": $APP_PID,
    "logFile": ".ai/qa/test-env-app.log"
  },
  "services": [
    {
      "type": "postgres",
      "database": "$QA_DB",
      "container": "",
      "disposable": true,
      "env": { "DATABASE_URL": "(in .ai/qa/run/.env — throwaway database for this run)" }
    }
  ],
  "credentials": [],
  "credentialsFile": ".ai/qa/test-env.env",
  "browser": {
    "provider": "playwright",
    "installed": true,
    "command": "node --experimental-default-type=commonjs (require('playwright') from $PW_NODE_PATH)",
    "version": "1.63.0",
    "descriptor": "",
    "notes": "No .ai/browsers descriptor in this repo — implicit Playwright provider, legacy embedded flow. Chromium needs LD_LIBRARY_PATH=$PW_LD_PATH and --no-sandbox (no root in this sandbox)."
  },
  "playwright": {
    "runner": "playwright",
    "installed": true,
    "config": "$PW_NODE_PATH",
    "browsers": ["chromium"]
  },
  "testRunner": { "name": "none", "config": "" },
  "platform": "linux",
  "startedAt": "$(date -u +%FT%TZ)",
  "notes": "App root is isolated at .ai/qa/run: its .env carries a throwaway database and NO RESEND_API_KEY, so claims never touch real lead data and never send email. Redis is deliberately unconfigured (the shared sandbox Redis holds the real lead-count cache key), so /api/health reports redis degraded while returning 200. Teardown drops database $QA_DB. Playwright: export LD_LIBRARY_PATH=$PW_LD_PATH and launch chromium with --no-sandbox."
}
EOF

printf '%s\n' "$QA_DB" > "$QA_DIR/test-env-db.name"

echo "TEST_ENV_STATUS=running"
echo "TEST_ENV_BASE_URL=$BASE_URL"
echo "TEST_ENV_DESCRIPTOR=.ai/qa/test-env.json"
echo "TEST_ENV_REUSED=0"
echo "BROWSER_PROVIDER=playwright"
echo "BROWSER_INSTALLED=1"

# The app serves from in-repo build artifacts; keep the lock for the environment's lifetime so a
# second bootstrap cannot republish underneath it.
RELEASE_LOCK=0
