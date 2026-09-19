#!/usr/bin/env bash
# Makes sure PostgreSQL is reachable before the app talks to it, and recovers the
# one failure mode the sandbox reliably produces on its own.
#
# The workspace is snapshot and resumed rather than shut down, so $PGDATA and
# /tmp survive a restart while every PID is reassigned. PostgreSQL's stale-lock
# detection is only kill(pid, 0) against the PID recorded in postmaster.pid, so
# when that PID has been recycled by an unrelated live process -- pm2's node
# supervisor reliably lands on the low PIDs a previous postmaster held -- the
# probe succeeds, PostgreSQL assumes another postmaster owns the data directory
# and refuses to start:
#
#   FATAL: lock file "postmaster.pid" already exists
#   HINT:  Is another postmaster (PID 186) running in data directory ...
#
# PM2 then burns its restart budget and parks the service in `errored`, and every
# request 500s on "Failed to connect to 127.0.0.1:5432". The real fix belongs in
# the image's /usr/local/bin/workspace-postgres, which is root-owned; until that
# lands, this clears the stale locks and nudges the service back up.
#
# Everything here is best effort and sandbox-shaped: outside the sandbox (no
# writable $PGDATA, no pm2 service) there is nothing to recover, so the script
# says so and returns immediately instead of waiting -- a plain clone pointing
# DATABASE_URL at Docker or a managed instance is never delayed by this guard.
#
# scripts/postgres.test.sh covers the pure helpers.
set -euo pipefail

root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"

PGDATA="${PGDATA:-/var/lib/postgresql/data/pgdata}"
SOCKET_DIR="${WORKSPACE_POSTGRES_SOCKET_DIR:-/tmp/workspace-postgres}"
WAIT_TIMEOUT="${PG_WAIT_TIMEOUT:-60}"

log() {
  echo "· $*" >&2
}

# DATABASE_URL is read by the app from .env, so fall back to the same file here
# rather than making the caller export it. DotEnv.Load only sets a key that is
# still unset, so the FIRST assignment wins there -- match that, and trim the
# value the same way, or a CRLF file leaves a \r in the hostname.
database_url() {
  if [ -n "${DATABASE_URL:-}" ]; then
    printf '%s' "$DATABASE_URL"
    return 0
  fi
  local file value
  # .env.local wins where it sets the key, matching the note at the top of .env;
  # an override file that only sets other keys must still fall through to .env.
  for file in "${root}/.env.local" "${root}/.env"; do
    [ -f "$file" ] || continue
    value="$(sed -n 's/^[[:space:]]*\(export[[:space:]]\+\)\?DATABASE_URL=//p' "$file" | head -n 1)"
    value="$(printf '%s' "$value" | tr -d '\r' | sed -e 's/[[:space:]]*$//')"
    value="${value%\"}"; value="${value#\"}"
    value="${value%\'}"; value="${value#\'}"
    if [ -n "$value" ]; then
      printf '%s' "$value"
      return 0
    fi
  done
}

# Sets PGHOST/PGPORT from a connection string, accepting both forms the app
# accepts (ConnectionUrls.ToNpgsqlConnectionString passes a key=value string
# straight through to Npgsql). Split the userinfo on the LAST '@', as
# System.Uri does, so a password containing '@' does not become the host.
parse_host_port() {
  local value="$1" rest="" hostport=""
  PGHOST=""
  PGPORT=""
  case "$value" in
    *://*)
      rest="${value#*://}"
      rest="${rest##*@}"
      hostport="${rest%%[/?]*}"
      PGHOST="${hostport%%:*}"
      case "$hostport" in *:*) PGPORT="${hostport##*:}" ;; esac
      ;;
    *=*)
      # key=value form: Host=...;Port=...;..., keys are case-insensitive.
      PGHOST="$(printf '%s' "$value" | tr ';' '\n' |
        sed -n 's/^[[:space:]]*[Hh][Oo][Ss][Tt][[:space:]]*=[[:space:]]*//p' | head -n 1)"
      PGPORT="$(printf '%s' "$value" | tr ';' '\n' |
        sed -n 's/^[[:space:]]*[Pp][Oo][Rr][Tt][[:space:]]*=[[:space:]]*//p' | head -n 1)"
      ;;
  esac
  case "$PGHOST" in '' | *[!a-zA-Z0-9.-]*) PGHOST=127.0.0.1 ;; esac
  case "$PGPORT" in '' | *[!0-9]*) PGPORT=5432 ;; esac
}

parse_host_port "$(database_url || true)"

# Sourced by scripts/postgres.test.sh, which only exercises the pure helpers.
if [ -n "${PG_GUARD_SOURCE_ONLY:-}" ]; then
  return 0
fi

# Pure bash so this works without libpq installed; pg_isready is not guaranteed
# to exist on a developer machine that only runs the client.
reachable() {
  (exec 3<>"/dev/tcp/${PGHOST}/${PGPORT}") >/dev/null 2>&1
}

# A postmaster configured with listen_addresses = '' answers on its unix socket
# and nothing else, so TCP silence alone does not mean "down". /proc/net/unix
# lists a socket only while a live process holds it -- a leftover socket file
# from a dead postmaster does not appear -- so this distinguishes "alive but not
# listening on TCP" from "gone", without needing pg_isready or ss.
socket_listening() {
  local socket="${SOCKET_DIR}/.s.PGSQL.${PGPORT}"
  [ -S "$socket" ] || return 1
  [ -r /proc/net/unix ] || return 1
  # Exact match on the path field: a substring match would read a neighbouring
  # cluster's live .s.PGSQL.54321 as ours on port 5432.
  awk -v want="$socket" '$NF == want { hit = 1; exit } END { exit !hit }' /proc/net/unix
}

# True only when $1 is a PostgreSQL process attached to OUR data directory.
# Both tests matter: /proc/<pid> also resolves for non-main threads, which is
# what fools PostgreSQL here, and an unrelated cluster may legitimately be
# running from a different PGDATA on another port.
pid_owns_pgdata() {
  local pid="$1" cmd="" cwd=""
  case "$pid" in
    '' | *[!0-9]*) return 1 ;;
  esac
  [ -r "/proc/${pid}/cmdline" ] || return 1
  cmd="$(tr '\0' ' ' <"/proc/${pid}/cmdline" 2>/dev/null || true)"
  case "$cmd" in
    *postgres* | *postmaster*) ;;
    *) return 1 ;;
  esac
  cwd="$(readlink "/proc/${pid}/cwd" 2>/dev/null || true)"
  [ "$cwd" = "$PGDATA" ] && return 0
  case "$cmd" in
    *"$PGDATA"*) return 0 ;;
  esac
  return 1
}

# Counts what recovery actually did, so the wait below is only entered when
# something changed. Waiting on a state nobody touched can only ever time out.
actions_taken=0

# $1 = lock file, $2 = label. The PID is always the first line.
reap_lock_file() {
  local file="$1" label="$2" pid=""
  [ -e "$file" ] || return 0
  pid="$(head -n 1 "$file" 2>/dev/null || true)"
  if pid_owns_pgdata "$pid"; then
    return 0
  fi
  log "clearing stale ${label} (PID ${pid:-unknown} is not a postmaster for ${PGDATA})"
  rm -f "$file"
  actions_taken=$(( actions_taken + 1 ))
}

reap_stale_locks() {
  [ -d "$PGDATA" ] && [ -w "$PGDATA" ] || return 0
  reap_lock_file "${PGDATA}/postmaster.pid" "postmaster.pid"
  reap_lock_file "${SOCKET_DIR}/.s.PGSQL.${PGPORT}.lock" "socket lock"
  # The socket carries no PID of its own. It is only an orphan once the lock is
  # gone and nothing is answering, and postgres will not reuse a socket path it
  # did not create.
  if [ -S "${SOCKET_DIR}/.s.PGSQL.${PGPORT}" ] &&
    [ ! -e "${SOCKET_DIR}/.s.PGSQL.${PGPORT}.lock" ]; then
    rm -f "${SOCKET_DIR}/.s.PGSQL.${PGPORT}"
    actions_taken=$(( actions_taken + 1 ))
  fi
}

# PM2 has usually given up by now (`errored`, restart budget spent), so clearing
# the locks is not enough on its own — the service needs an explicit nudge.
restart_service() {
  command -v pm2 >/dev/null 2>&1 || return 0
  pm2 describe postgres >/dev/null 2>&1 || return 0
  log "restarting the postgres service"
  pm2 restart postgres >/dev/null 2>&1 || true
  actions_taken=$(( actions_taken + 1 ))
}

reachable && exit 0

if socket_listening; then
  # Alive, just not on TCP. Reaping its lock files or bouncing the service would
  # take a working database down; the app's DATABASE_URL is the thing to fix.
  log "postgres is listening on ${SOCKET_DIR}/.s.PGSQL.${PGPORT} but not on ${PGHOST}:${PGPORT} — leaving it alone"
  exit 0
fi

log "postgres is not answering on ${PGHOST}:${PGPORT} — attempting recovery"
reap_stale_locks
restart_service

if [ "$actions_taken" -eq 0 ]; then
  # Nothing here to recover: no writable $PGDATA and no pm2 service, which is
  # every environment except the sandbox. Waiting out the timeout would only
  # delay the caller's own connection error by ${WAIT_TIMEOUT}s.
  log "nothing to recover (no writable ${PGDATA}, no pm2 postgres service) — continuing without waiting"
  exit 0
fi

deadline=$(( $(date +%s) + WAIT_TIMEOUT ))
while ! reachable; do
  if [ "$(date +%s)" -ge "$deadline" ]; then
    # Deliberately non-fatal, matching the sandbox entrypoint: a broken database
    # should still leave a running process and a readable error to diagnose it.
    log "postgres still unreachable after ${WAIT_TIMEOUT}s — continuing anyway"
    exit 0
  fi
  sleep 1
done

log "postgres is accepting connections on ${PGHOST}:${PGPORT}"
