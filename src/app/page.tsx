import TaskBoard from '@/components/TaskBoard'
import { pool } from '@/lib/db'
import { redis } from '@/lib/redis'

export const dynamic = 'force-dynamic'

async function probe(fn: () => Promise<unknown>) {
  try {
    await fn()
    return 'connected'
  } catch {
    return 'unavailable'
  }
}

export default async function Home() {
  const [postgres, cache] = await Promise.all([
    probe(() => pool.query('select 1')),
    probe(() => redis.ping()),
  ])

  return (
    <main>
      <h1>Task board</h1>
      <p className="sub">
        Next.js App Router on the sandbox PostgreSQL and Redis. PostgreSQL: <code>{postgres}</code> · Redis:{' '}
        <code>{cache}</code>
      </p>
      <TaskBoard />
    </main>
  )
}
