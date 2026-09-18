import { NextResponse } from 'next/server'
import { createTask, listTasks, type Task } from '@/lib/db'
import { TASKS_CACHE_KEY, dropCache, readCache, writeCache } from '@/lib/redis'

export const dynamic = 'force-dynamic'

export async function GET() {
  const cached = await readCache<Task[]>(TASKS_CACHE_KEY)
  if (cached) return NextResponse.json({ tasks: cached, source: 'redis' })

  const tasks = await listTasks()
  await writeCache(TASKS_CACHE_KEY, tasks)
  return NextResponse.json({ tasks, source: 'postgres' })
}

export async function POST(request: Request) {
  const body = await request.json().catch(() => null)
  const title = typeof body?.title === 'string' ? body.title.trim() : ''
  if (!title) {
    return NextResponse.json({ error: 'title is required' }, { status: 400 })
  }

  const task = await createTask(title)
  await dropCache(TASKS_CACHE_KEY)
  return NextResponse.json({ task }, { status: 201 })
}
