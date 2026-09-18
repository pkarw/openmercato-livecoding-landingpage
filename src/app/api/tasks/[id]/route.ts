import { NextResponse } from 'next/server'
import { deleteTask, updateTaskStatus, type Task } from '@/lib/db'
import { TASKS_CACHE_KEY, dropCache } from '@/lib/redis'

export const dynamic = 'force-dynamic'

const STATUSES: Task['status'][] = ['todo', 'doing', 'done']

type Context = { params: Promise<{ id: string }> }

export async function PATCH(request: Request, { params }: Context) {
  const id = Number((await params).id)
  if (!Number.isInteger(id)) {
    return NextResponse.json({ error: 'invalid id' }, { status: 400 })
  }

  const body = await request.json().catch(() => null)
  const status = body?.status
  if (!STATUSES.includes(status)) {
    return NextResponse.json({ error: `status must be one of ${STATUSES.join(', ')}` }, { status: 400 })
  }

  const task = await updateTaskStatus(id, status)
  if (!task) return NextResponse.json({ error: 'not found' }, { status: 404 })

  await dropCache(TASKS_CACHE_KEY)
  return NextResponse.json({ task })
}

export async function DELETE(_request: Request, { params }: Context) {
  const id = Number((await params).id)
  if (!Number.isInteger(id)) {
    return NextResponse.json({ error: 'invalid id' }, { status: 400 })
  }

  const removed = await deleteTask(id)
  if (!removed) return NextResponse.json({ error: 'not found' }, { status: 404 })

  await dropCache(TASKS_CACHE_KEY)
  return new NextResponse(null, { status: 204 })
}
