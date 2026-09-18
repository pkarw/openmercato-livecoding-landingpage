import { NextResponse } from 'next/server'
import { pool } from '@/lib/db'
import { redis } from '@/lib/redis'

export const dynamic = 'force-dynamic'

async function check(probe: () => Promise<unknown>) {
  try {
    await probe()
    return { ok: true as const }
  } catch (err) {
    return { ok: false as const, error: err instanceof Error ? err.message : String(err) }
  }
}

export async function GET() {
  const [postgres, cache] = await Promise.all([
    check(() => pool.query('select 1')),
    check(() => redis.ping()),
  ])

  return NextResponse.json(
    { status: postgres.ok && cache.ok ? 'ok' : 'degraded', postgres, redis: cache },
    { status: postgres.ok ? 200 : 503 }
  )
}
