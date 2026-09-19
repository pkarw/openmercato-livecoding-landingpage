<div align="center">

<img src="client/public/brand/openmercato-mark.svg" alt="Open Mercato" width="64" />
&nbsp;&nbsp;&nbsp;
<img src="client/public/brand/aitechleaders-logo.png" alt="AI Tech Leaders by BRAVE" height="56" />

# openmercato-livecoding-landingpage

**A one-page lead magnet for the −10% offer on [openmercatocloud.com](https://openmercatocloud.com/) and the
[aitechleaders.pl](https://aitechleaders.pl/) training — live until the end of Sunday, 20.09.**

ASP.NET Core (API + pages) · PostgreSQL with migrations · React + shadcn/ui · everything on port **3000**

</div>

---

## What it does

Visitors pick **Open Mercato Cloud**, the **AI Tech Leaders** training, or both; leave their email; and accept the
[privacy policy](https://openmercatocloud.com/privacy) plus marketing consent for both brands. The choice and both
consents are stored in PostgreSQL, a personal discount code is reserved server-side, and Resend sends a confirmation
to the lead and a notification to the sales inbox. The code itself is **not** shown on screen — it is emailed later
and redeemed at sign-up.

- Countdown to the deadline, live claim counter (cached in Redis)
- Open Mercato Cloud look and feel: `#141313` canvas, `#e5f520` accent, Inter + Caveat
- Consent-gated submit — the button stays disabled until both boxes are ticked
- One discount per email address; a repeat submit returns the original reservation

## Stack

| Layer | Choice |
| --- | --- |
| Backend | ASP.NET Core 10 minimal APIs (`server/`), Kestrel on a single port |
| Data | PostgreSQL via Npgsql + Dapper, SQL migrations with a checksum-tracking runner |
| Cache | Redis via StackExchange.Redis, best-effort — a cache outage never breaks a page load |
| Email | Resend HTTP API (`server/Notifications/`) |
| Frontend | React 19 + Vite + TypeScript + Tailwind v4 + shadcn/ui (`client/`) |

One process serves everything: `/api/*` hits the minimal APIs, every other path falls back to the React bundle in
`server/wwwroot`. No second port, no reverse proxy.

## Getting started

```bash
bash scripts/build.sh          # installs the .NET SDK if missing, builds the client, publishes the server
bash scripts/serve.sh db migrate
bash scripts/serve.sh          # http://localhost:3000
```

Working on the UI? Run the API and Vite side by side — Vite proxies `/api` to 3000:

```bash
scripts/dotnet.sh run --project server     # API + built client on :3000
npm --prefix client run dev                # hot reload on :5173 (dev only)
```

## Database migrations

`db/migrations/*.sql` run in filename order, once each, inside a transaction, tracked in `schema_migrations`
together with a SHA-256 checksum — so editing a migration that already ran is reported instead of silently ignored.

```bash
bash scripts/serve.sh db migrate          # apply everything pending
bash scripts/serve.sh db status           # ✓ applied · pending ! changed since it ran
bash scripts/serve.sh db new add_utm      # scaffold db/migrations/<timestamp>_add_utm.sql
```

| Migration | Purpose |
| --- | --- |
| `001_init.sql` | the task board this repository started from |
| `002_leads.sql` | `leads` — email, interest, consents, reserved code, source |
| `003_drop_task_board.sql` | drops the old `tasks` table |
| `004_lead_notification_deliveries.sql` | durable, independently retried lead and inbox email deliveries |

## API

| Method | Path | Description |
| --- | --- | --- |
| `GET` | `/api/offer` | Discount percentage, deadline, whether it is still live, how many people claimed |
| `POST` | `/api/leads` | Reserve a discount: `{ email, name?, interest, privacyAccepted, marketingConsent, source? }` |
| `GET` | `/api/health` | PostgreSQL/Redis connectivity and notification queue status |

`interest` is `openmercato`, `aitechleaders` or `both`. Both consents are mandatory — the API rejects a claim
without them, and refuses anything sent after the deadline with `410 Gone`.

Email delivery is asynchronous after the reservation commits. PostgreSQL keeps one lead and one inbox delivery per
reservation, workers lease them safely across instances, and retryable provider failures use bounded backoff. The
health response reports pending/failed counts and whether Resend is configured; terminal failures also produce an
error log. Delivery rows are retained as the operational audit for this campaign; cleanup is an explicit operator
decision rather than an automatic expiry. The migration is additive, so an older binary ignores the queue during
rollback, but delivery pauses until the new binary returns. Drop `lead_notification_deliveries` only after deciding
that its pending/failed work can be discarded.

The endpoint is anonymous, so an address is never treated as proof of owning it: **a repeat claim returns the
existing reservation and changes nothing** — not the stored interest, not the consents, not the name. Changing a
stored preference needs an ownership proof this endpoint does not have.

## Tests

`tests/Landing.Tests` runs against a real PostgreSQL. The fixture creates a throwaway `landing_test_<guid>`
database, applies `db/migrations/*.sql` through the app's own migration runner, and drops it afterwards.

```bash
export LANDING_TEST_DATABASE_URL=postgres://postgres@127.0.0.1:5432/postgres
bash scripts/dotnet.sh test tests/Landing.Tests
```

`LANDING_TEST_DATABASE_URL` is deliberately separate from `DATABASE_URL`: the tests never fall back to the
application's database, and must never be pointed at production.

## Continuous integration

`.github/workflows/ci.yml` runs on every push to `main` and every pull request:

| Job | What it runs |
| --- | --- |
| Client | `npm --prefix client ci`, `run typecheck`, `run build` |
| Server | `scripts/dotnet.sh build server`, then `scripts/dotnet.sh test tests/Landing.Tests` against a `postgres:17` service container |

## Environment

Copy `.env.example` to `.env` and fill in the secrets; machine-specific overrides belong in `.env.local`.
Both `.env` and `.env.local` are git-ignored — never commit a real `RESEND_API_KEY`.

| Variable | Purpose |
| --- | --- |
| `DATABASE_URL` | PostgreSQL connection string (URL form is converted for Npgsql) |
| `REDIS_URL` | Redis connection string; leave empty to run without a cache |
| `CACHE_TTL_SECONDS` | How long the claim counter stays cached (default 30) |
| `PORT` | Single HTTP port for API + pages (default 3000) |
| `OFFER_DISCOUNT_PERCENT` | Headline discount (default 10) |
| `OFFER_ENDS_AT` | Deadline, ISO 8601 (default `2026-09-20T23:59:59+02:00`) |
| `RESEND_API_KEY` | Resend API key; unset disables sending without breaking the form |
| `ADMIN_EMAIL` | From-address, must be on a domain verified in Resend |
| `LEADS_INBOX` | Where lead notifications land (default `info@openmercato.com`) |

### Credential safety

CI runs Gitleaks against the checked-out tree and proves the detector with a generated fake-secret fixture. To run the same checks locally, install Gitleaks and use:

```bash
scripts/test-secret-scan.sh
scripts/check-secrets.sh
```

If a real credential reaches Git, removing the file is not enough. Revoke or rotate the credential with its service owner, update deployment secret storage, and review repository access and history privately. Coordinate any history rewrite with every clone owner; the current-tree scan deliberately does not claim that existing history is clean.

## Layout

```
client/            React + shadcn/ui landing page (Vite → server/wwwroot)
  src/components/  Brand logos, countdown, offer picker, claim form
  public/brand/    Open Mercato mark and the BRAVE wordmark
server/            ASP.NET Core app — minimal APIs, static hosting, SPA fallback
  Data/            Dapper repository, migration runner, db CLI verbs
  Notifications/   Resend client and the two lead emails
db/migrations/     SQL migrations, applied in filename order
scripts/           dotnet.sh (SDK bootstrap), postgres.sh (db guard, + postgres.test.sh), build.sh, serve.sh
```

## Sandbox

`openmercato.toml` builds the client, publishes the server, applies migrations, and previews
`scripts/serve.sh` on `0.0.0.0:3000`. Apply changes with `workspace-agent-cli start`.

The sandbox is snapshotted and resumed rather than shut down, so PostgreSQL can come back with a
`postmaster.pid` naming a PID that has since been recycled by another process. PostgreSQL then
refuses to start (`lock file "postmaster.pid" already exists`), PM2 exhausts its restart budget, and
every request fails on `Failed to connect to 127.0.0.1:5432`. `scripts/serve.sh` runs
`scripts/postgres.sh` first, which clears provably stale lock files and restarts the service. It is a
no-op when the database is already up, and anywhere there is nothing to recover — no writable
`$PGDATA` and no pm2 service — so a clone pointing `DATABASE_URL` at Docker or a managed instance is
never delayed by it. A postmaster that is listening only on its unix socket counts as up and is left
alone. The durable fix belongs in the image's root-owned `/usr/local/bin/workspace-postgres`.

Run `bash scripts/postgres.test.sh` after touching the guard: it covers the connection-string forms
the app accepts, `.env` precedence, and that neither branch waits or deletes anything it should not.
