# om-prepare-test-env — repo-local notes (-15% landing page)

Generated 2026-09-18. The entrypoint is `.ai/scripts/test-env-up.sh` /
`.ai/scripts/test-env-down.sh`; run those, do not boot by hand.

## The working chain

```sh
sh .ai/scripts/test-env-up.sh        # cold ~7s (after client deps), warm ~0.03s, reuses
sh .ai/scripts/test-env-down.sh      # stops the app, drops the run's throwaway database
```

Cold run does: `npm --prefix client ci` (only when `client/node_modules` is missing) →
`npm --prefix client run build` (Vite writes into `server/wwwroot`) →
`scripts/dotnet.sh publish server -c Release -o .ai/qa/run/.publish` →
`create database landing_qa_<runid>` on the repository's PostgreSQL →
`<publish> db migrate` → start the app → wait on `/api/health`.

## Two failures the script now prevents

**1. Sourcing `.env` leaks production configuration into the app.**
`Landing.Configuration.DotEnv` only fills variables that are *unset* — a real environment
variable always wins over the `.env` file it reads. The first generated script did
`set -a; . "$PROJECT_ROOT/.env"; set +a` to get the PostgreSQL URL, which exported the
repository's real `DATABASE_URL`, `RESEND_API_KEY` and `PORT` into the app it then
launched. The app bound the already-taken port 3000 and ran its migration against the
**real** database instead of the throwaway one.

The script now reads the file in a subshell and clears those variables for every app
invocation:

```sh
ADMIN_DB_URL=$(sh -c 'set -a; . "$1"; set +a; printf "%s" "${DATABASE_URL:-}"' _ "$PROJECT_ROOT/.env")
APP_ENV="env -u DATABASE_URL -u REDIS_URL -u PORT -u RESEND_API_KEY -u ADMIN_EMAIL -u LEADS_INBOX -u CACHE_TTL_SECONDS -u OFFER_DISCOUNT_PERCENT -u OFFER_ENDS_AT"
```

**2. The app must never load the repository's `.env` at all.**
`.env` is tracked in this repository and carries the real `RESEND_API_KEY`; a QA claim
would email the lead and the inbox for real. `DotEnv.FindRepositoryRoot` walks up from the
binaries looking for `openmercato.toml`, so the script publishes into an isolated root
`.ai/qa/run/` containing its own `openmercato.toml`, `db/migrations`, and a `.env` with
disposable values only — **no `RESEND_API_KEY`**, which makes `EmailSettings.IsConfigured`
false and turns every send into a logged skip. Do not "simplify" this to a `.env.local`
override: `Environment.SetEnvironmentVariable(key, "")` deletes the variable, so an empty
`RESEND_API_KEY=` in a `.env.local` silently falls through to the real key in `.env`.

Redis is deliberately left unconfigured: the shared sandbox Redis holds the live app's
lead-count cache key. `/api/health` therefore reports `redis` degraded while still
answering `200` (the probe only requires PostgreSQL).

## Playwright in this sandbox (no root)

Browsers are cached at `~/.cache/ms-playwright` (chromium revision **1243** → the
`playwright` npm package **1.63.x**; 1.56 wants revision 1194 and will not launch).
`playwright install-deps` needs root, which the sandbox does not have, so chromium's
system libraries were unpacked from Debian bookworm `.deb` files into `~/.local/pwlibs`:

```sh
# libnspr4 libnss3 libxcomposite1 libxdamage1 libxfixes3 libxrandr2 libasound2
# libatk1.0-0 libatk-bridge2.0-0 libatspi2.0-0 libdbus-1-3 libgbm1 libxkbcommon0
# libxi6 libdrm2 libwayland-server0 libcups2 libavahi-client3 libavahi-common3
export LD_LIBRARY_PATH="$HOME/.local/pwlibs/usr/lib/x86_64-linux-gnu:$HOME/.local/pwlibs/lib/x86_64-linux-gnu"
node -e "require('playwright').chromium.launch({args:['--no-sandbox','--disable-dev-shm-usage']})"
```

Without `LD_LIBRARY_PATH` the launch fails with
`libnspr4.so: cannot open shared object file`; without `--no-sandbox` it fails with
`Target page, context or browser has been closed`.

## Worth committing

`.ai/` is untracked in this repository, so these scripts do not survive a fresh clone.
Committing `.ai/scripts/test-env-*.sh` would give every checkout the same one-command
environment.

## Two more things the scripts had to learn

- **Start the app with `setsid`.** Backgrounded with a plain `&`, the app died the moment the
  invoking shell went away and the next probe got `ECONNREFUSED`.
- **Teardown cannot trust the recorded PID alone.** `setsid` may fork, so the down script also
  matches `pgrep -f "$QA_ROOT/.publish/"` — an absolute path, which deliberately cannot match
  the sandbox's own app on port 3000 (it runs from a relative `.publish/`).

## Tooling gap

`jq` is **not** installed in this sandbox, although `.ai/trackers/github.md` lists it as a
prerequisite and its `attach-image-evidence` snippet uses `jq --rawfile` to build the Contents
API body. Build that JSON with `python3` instead (`base64` + `json.dumps` to a file, then
`gh api -X PUT … --input <file>`); an image-sized base64 string must never reach a command line.
