# open-mercato-custom-app

A Next.js (App Router, TypeScript) starter wired to the Open Mercato sandbox's PostgreSQL and Redis.
It ships a small task board: CRUD over PostgreSQL, list responses cached in Redis, and a health
endpoint that reports both backends.

## Stack

- Next.js 16 / React 19 / TypeScript
- PostgreSQL via `pg` (`src/lib/db.ts`)
- Redis via `ioredis`, best-effort cache (`src/lib/redis.ts`)
- Dependency-free SQL migration runner (`scripts/db.mjs`)

## Getting started

```bash
npm install                  # defaults live in .env; override them in .env.local
npm run db:setup             # migrate + seed
npm run dev                  # http://localhost:3000
```

Production-style run:

```bash
npm run build
npm start                    # binds 0.0.0.0:3000
```

## Environment

| Variable | Purpose |
| --- | --- |
| `DATABASE_URL` | PostgreSQL connection string |
| `REDIS_URL` | Redis connection string |
| `CACHE_TTL_SECONDS` | How long the task list stays cached (default 30) |

Defaults ship in `.env`; machine-specific overrides belong in `.env.local`.

## API

| Method | Path | Description |
| --- | --- | --- |
| `GET` | `/api/tasks` | List tasks; response includes `source: postgres \| redis` |
| `POST` | `/api/tasks` | Create a task from `{ "title": "..." }` |
| `PATCH` | `/api/tasks/:id` | Set status to `todo`, `doing`, or `done` |
| `DELETE` | `/api/tasks/:id` | Delete a task |
| `GET` | `/api/health` | PostgreSQL and Redis connectivity |

## Layout

```
db/migrations/     SQL migrations, applied in filename order
scripts/db.mjs     migrate + seed runner
src/app/           routes, layout, global styles
src/app/api/       route handlers
src/components/    client components
src/lib/           database and cache clients
```

## Sandbox

`openmercato.toml` (version 2) installs dependencies, runs migrations and seeds, builds, and then
serves the production build on `0.0.0.0:3000`. Apply changes with `workspace-agent-cli start`.
