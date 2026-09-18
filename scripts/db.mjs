#!/usr/bin/env node
// Tiny migration/seed runner: applies db/migrations/*.sql once each, tracked in
// schema_migrations. Keeps the starter dependency-free — swap for a real
// migration tool when the schema grows.
import { readFile, readdir } from 'node:fs/promises'
import { existsSync } from 'node:fs'
import path from 'node:path'
import { fileURLToPath } from 'node:url'
import pg from 'pg'

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..')

async function loadEnv() {
  for (const file of ['.env.local', '.env']) {
    const full = path.join(root, file)
    if (!existsSync(full)) continue
    const contents = await readFile(full, 'utf8')
    for (const line of contents.split('\n')) {
      const match = line.match(/^\s*([A-Za-z_][A-Za-z0-9_]*)\s*=\s*(.*)\s*$/)
      if (!match || line.trimStart().startsWith('#')) continue
      const value = match[2].replace(/^["']|["']$/g, '')
      if (process.env[match[1]] === undefined) process.env[match[1]] = value
    }
  }
}

async function migrate(client) {
  await client.query(
    'create table if not exists schema_migrations (name text primary key, applied_at timestamptz not null default now())'
  )
  const dir = path.join(root, 'db', 'migrations')
  const files = (await readdir(dir)).filter((f) => f.endsWith('.sql')).sort()
  const { rows } = await client.query('select name from schema_migrations')
  const applied = new Set(rows.map((r) => r.name))

  for (const file of files) {
    if (applied.has(file)) {
      console.log(`· ${file} (already applied)`)
      continue
    }
    const sql = await readFile(path.join(dir, file), 'utf8')
    await client.query('begin')
    try {
      await client.query(sql)
      await client.query('insert into schema_migrations (name) values ($1)', [file])
      await client.query('commit')
      console.log(`✓ ${file}`)
    } catch (err) {
      await client.query('rollback')
      throw err
    }
  }
}

async function seed(client) {
  const { rows } = await client.query('select count(*)::int as count from tasks')
  if (rows[0].count > 0) {
    console.log(`· seed skipped (${rows[0].count} tasks already present)`)
    return
  }
  await client.query(
    `insert into tasks (title, status) values
       ('Read the README', 'done'),
       ('Wire a new API route', 'doing'),
       ('Ship something', 'todo')`
  )
  console.log('✓ seeded 3 tasks')
}

async function main() {
  const command = process.argv[2]
  if (!['migrate', 'seed'].includes(command)) {
    console.error('usage: node scripts/db.mjs <migrate|seed>')
    process.exit(1)
  }

  await loadEnv()
  if (!process.env.DATABASE_URL) {
    console.error('DATABASE_URL is not set (copy .env.example to .env.local)')
    process.exit(1)
  }

  const client = new pg.Client({ connectionString: process.env.DATABASE_URL })
  await client.connect()
  try {
    if (command === 'migrate') await migrate(client)
    else await seed(client)
  } finally {
    await client.end()
  }
}

main().catch((err) => {
  console.error(err)
  process.exit(1)
})
