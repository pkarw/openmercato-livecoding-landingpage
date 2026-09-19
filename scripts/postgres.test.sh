#!/usr/bin/env bash
# Self-test for scripts/postgres.sh. Plain bash on purpose: the repository has no
# test runner, and this needs none -- run it with `bash scripts/postgres.test.sh`.
#
# Two things are worth pinning down. The connection-string parse has to agree with
# what the app itself accepts (server/Configuration/ConnectionUrls.cs takes both a
# URL and a key=value string, and System.Uri splits the userinfo on the LAST '@'),
# and the recovery path must not wait for a state it never touched.
set -uo pipefail

root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
guard="${root}/scripts/postgres.sh"

failures=0
stub_dir="$(mktemp -d)"
trap 'rm -rf "$stub_dir"' EXIT

# A pm2 that reports no postgres service, so no test can touch a real one.
printf '#!/bin/sh\nexit 1\n' >"${stub_dir}/pm2"
chmod +x "${stub_dir}/pm2"

check() {
  local label="$1" expected="$2" actual="$3"
  if [ "$expected" = "$actual" ]; then
    printf 'ok   %s\n' "$label"
  else
    printf 'FAIL %s\n       expected: %s\n       actual:   %s\n' "$label" "$expected" "$actual"
    failures=$(( failures + 1 ))
  fi
}

# --- connection-string parsing -------------------------------------------------
# Source the guard with only the helpers defined, then call parse_host_port
# directly; PG_GUARD_SOURCE_ONLY stops it before it probes anything.
parsed() {
  (
    PG_GUARD_SOURCE_ONLY=1
    # shellcheck source=/dev/null
    . "$guard"
    parse_host_port "$1"
    printf '%s:%s' "$PGHOST" "$PGPORT"
  )
}

check "url form"                  "db.example.com:6432" "$(parsed 'postgres://u:p@db.example.com:6432/x')"
check "url without a port"        "db.example.com:5432" "$(parsed 'postgres://u:p@db.example.com/x')"
check "url without credentials"   "db.example.com:6432" "$(parsed 'postgres://db.example.com:6432/x')"
check "password containing @"     "db.example.com:6432" "$(parsed 'postgres://user:p@ss@db.example.com:6432/x')"
check "url with a query string"   "db.example.com:6432" "$(parsed 'postgres://u:p@db.example.com:6432/x?sslmode=require')"
check "key=value form"            "db.example.com:6432" "$(parsed 'Host=db.example.com;Port=6432;Database=x;Username=u')"
check "key=value, lowercase keys" "db.example.com:6432" "$(parsed 'host=db.example.com;port=6432;database=x')"
check "key=value without a port"  "db.example.com:5432" "$(parsed 'Host=db.example.com;Database=x')"
check "unparseable falls back"    "127.0.0.1:5432"      "$(parsed 'nonsense')"
check "empty falls back"          "127.0.0.1:5432"      "$(parsed '')"

# --- .env precedence -----------------------------------------------------------
# DotEnv.Load only sets a key that is still unset, so the FIRST assignment wins
# and .env.local beats .env; a CRLF file must not leave \r in the hostname.
env_parsed() {
  local dir
  dir="$(mktemp -d)"
  [ -n "${1:-}" ] && printf '%b' "$1" >"${dir}/.env"
  [ -n "${2:-}" ] && printf '%b' "$2" >"${dir}/.env.local"
  (
    PG_GUARD_SOURCE_ONLY=1
    # shellcheck source=/dev/null
    . "$guard"
    root="$dir"
    unset DATABASE_URL
    parse_host_port "$(database_url || true)"
    printf '%s:%s' "$PGHOST" "$PGPORT"
  )
  rm -rf "$dir"
}

check ".env is read" "a.example.com:6432" \
  "$(env_parsed 'DATABASE_URL=postgres://u:p@a.example.com:6432/x\n')"
check ".env.local wins over .env" "b.example.com:6433" \
  "$(env_parsed 'DATABASE_URL=postgres://u:p@a.example.com:6432/x\n' 'DATABASE_URL=postgres://u:p@b.example.com:6433/x\n')"
check ".env.local without the key falls through" "a.example.com:6432" \
  "$(env_parsed 'DATABASE_URL=postgres://u:p@a.example.com:6432/x\n' 'REDIS_URL=redis://127.0.0.1:6379/1\n')"
check "first duplicate wins, as DotEnv.Load does" "a.example.com:6432" \
  "$(env_parsed 'DATABASE_URL=postgres://u:p@a.example.com:6432/x\nDATABASE_URL=postgres://u:p@z.example.com:6499/x\n')"
check "CRLF line endings are trimmed" "a.example.com:6432" \
  "$(env_parsed 'DATABASE_URL=postgres://u:p@a.example.com:6432/x\r\n')"
check "quoted value is unquoted" "a.example.com:6432" \
  "$(env_parsed 'DATABASE_URL="postgres://u:p@a.example.com:6432/x"\n')"
check "export prefix is accepted" "a.example.com:6432" \
  "$(env_parsed 'export DATABASE_URL=postgres://u:p@a.example.com:6432/x\n')"
check "commented-out key is ignored" "127.0.0.1:5432" \
  "$(env_parsed '# DATABASE_URL=postgres://u:p@a.example.com:6432/x\n')"

# --- the recovery path must not wait when it recovered nothing ------------------
# No writable $PGDATA and no pm2 service: there is nothing to wait for, so the
# guard must return promptly instead of burning $PG_WAIT_TIMEOUT.
unreachable_run() {
  local timeout="$1" started elapsed out
  started=$(date +%s)
  out="$(
    PATH="${stub_dir}:$PATH" \
      PGDATA="${stub_dir}/absent-pgdata" \
      WORKSPACE_POSTGRES_SOCKET_DIR="${stub_dir}/absent-sockets" \
      PG_WAIT_TIMEOUT="$timeout" \
      DATABASE_URL='postgres://u:p@127.0.0.1:65001/x' \
      bash "$guard" 2>&1
  )"
  elapsed=$(( $(date +%s) - started ))
  printf '%s|%s' "$elapsed" "$out"
}

result="$(unreachable_run 30)"
elapsed="${result%%|*}"
output="${result#*|}"
if [ "$elapsed" -le 5 ]; then
  printf 'ok   nothing to recover: returns in %ss, not the 30s timeout\n' "$elapsed"
else
  printf 'FAIL nothing to recover: took %ss, expected an immediate return\n' "$elapsed"
  failures=$(( failures + 1 ))
fi
case "$output" in
  *"nothing to recover"*) printf 'ok   nothing to recover: says so\n' ;;
  *) printf 'FAIL nothing to recover: unexpected output: %s\n' "$output"; failures=$(( failures + 1 )) ;;
esac

# --- a socket-only postmaster is alive, an orphaned socket file is not ---------
# The dangerous mistake would be bouncing (or unlocking) a postmaster that is
# running with listen_addresses = '' just because TCP is silent.
socket_run() {
  local live="$1" out sockets pgdata
  # A unix socket path cannot exceed ~108 bytes, so keep this one short rather
  # than hanging it off $TMPDIR, which may already be deep.
  sockets="$(TMPDIR=/tmp mktemp -d)"
  pgdata="${sockets}-pgdata"
  mkdir -p "$sockets" "$pgdata"
  printf '999999\n' >"${pgdata}/postmaster.pid"
  if [ "$live" = live ]; then
    python3 -c "
import socket, time
s = socket.socket(socket.AF_UNIX)
s.bind('${sockets}/.s.PGSQL.65001')
s.listen(1)
time.sleep(10)
" &
    local listener=$!
    sleep 1
  else
    # A leftover socket file from a dead postmaster: present on disk, held by nobody.
    python3 -c "
import socket
s = socket.socket(socket.AF_UNIX)
s.bind('${sockets}/.s.PGSQL.65001')
"
  fi
  out="$(PATH="${stub_dir}:$PATH" PGDATA="$pgdata" \
    WORKSPACE_POSTGRES_SOCKET_DIR="$sockets" PG_WAIT_TIMEOUT=2 \
    DATABASE_URL='postgres://u:p@127.0.0.1:65001/x' bash "$guard" 2>&1)"
  [ "$live" = live ] && { kill "$listener" 2>/dev/null || true; wait "$listener" 2>/dev/null || true; }
  # Report what happened plus whether the lock file survived.
  printf '%s' "$out"
  [ -e "${pgdata}/postmaster.pid" ] && printf ' [lock kept]' || printf ' [lock cleared]'
  rm -rf "$sockets" "$pgdata"
}

if command -v python3 >/dev/null 2>&1; then
  out="$(socket_run live)"
  case "$out" in
    *"leaving it alone"*"[lock kept]") printf 'ok   socket-only postmaster is left untouched\n' ;;
    *) printf 'FAIL socket-only postmaster: %s\n' "$out"; failures=$(( failures + 1 )) ;;
  esac
  out="$(socket_run dead)"
  case "$out" in
    *"clearing stale postmaster.pid"*"[lock cleared]") printf 'ok   orphaned socket file does not count as alive\n' ;;
    *) printf 'FAIL orphaned socket file: %s\n' "$out"; failures=$(( failures + 1 )) ;;
  esac
else
  printf 'skip socket liveness: no python3 to bind a unix socket\n'
fi

# --- an already-reachable database is an untouched, silent no-op ---------------
# Bind a listener and point the guard at it; it must exit 0 without logging.
port=""
for candidate in 65010 65011 65012; do
  if ! (exec 3<>"/dev/tcp/127.0.0.1/${candidate}") >/dev/null 2>&1; then
    port="$candidate"
    break
  fi
done
if [ -n "$port" ] && command -v python3 >/dev/null 2>&1; then
  python3 -c "
import socket, time, sys
s = socket.socket()
s.setsockopt(socket.SOL_SOCKET, socket.SO_REUSEADDR, 1)
s.bind(('127.0.0.1', ${port}))
s.listen(1)
time.sleep(10)
" &
  listener=$!
  sleep 1
  out="$(PATH="${stub_dir}:$PATH" PGDATA="${stub_dir}/absent-pgdata" \
    DATABASE_URL="postgres://u:p@127.0.0.1:${port}/x" bash "$guard" 2>&1)"
  rc=$?
  kill "$listener" 2>/dev/null || true
  wait "$listener" 2>/dev/null || true
  check "reachable database: exit status" "0" "$rc"
  check "reachable database: no output"   ""  "$out"
else
  printf 'skip reachable database: no free port or no python3\n'
fi

echo
if [ "$failures" -eq 0 ]; then
  echo "scripts/postgres.sh: all checks passed"
else
  echo "scripts/postgres.sh: ${failures} check(s) failed"
fi
exit $(( failures > 0 ))
