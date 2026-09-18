import { Pool } from 'pg'

const globalForDb = globalThis as unknown as { pgPool?: Pool }

export const pool =
  globalForDb.pgPool ??
  new Pool({
    connectionString: process.env.DATABASE_URL,
    max: 5,
    idleTimeoutMillis: 30_000,
  })

if (process.env.NODE_ENV !== 'production') globalForDb.pgPool = pool

export type Task = {
  id: number
  title: string
  status: 'todo' | 'doing' | 'done'
  created_at: string
}

export async function listTasks(): Promise<Task[]> {
  const { rows } = await pool.query<Task>(
    'select id, title, status, created_at from tasks order by id desc'
  )
  return rows
}

export async function createTask(title: string): Promise<Task> {
  const { rows } = await pool.query<Task>(
    'insert into tasks (title) values ($1) returning id, title, status, created_at',
    [title]
  )
  return rows[0]
}

export async function updateTaskStatus(id: number, status: Task['status']): Promise<Task | null> {
  const { rows } = await pool.query<Task>(
    'update tasks set status = $2 where id = $1 returning id, title, status, created_at',
    [id, status]
  )
  return rows[0] ?? null
}

export async function deleteTask(id: number): Promise<boolean> {
  const { rowCount } = await pool.query('delete from tasks where id = $1', [id])
  return (rowCount ?? 0) > 0
}
