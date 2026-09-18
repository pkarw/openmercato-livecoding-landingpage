'use client'

import { useCallback, useEffect, useState } from 'react'
import type { Task } from '@/lib/db'

const NEXT_STATUS: Record<Task['status'], Task['status']> = {
  todo: 'doing',
  doing: 'done',
  done: 'todo',
}

export default function TaskBoard() {
  const [tasks, setTasks] = useState<Task[]>([])
  const [source, setSource] = useState<string>('')
  const [title, setTitle] = useState('')
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState('')

  const load = useCallback(async () => {
    try {
      const res = await fetch('/api/tasks', { cache: 'no-store' })
      const data = await res.json()
      if (!res.ok) throw new Error(data.error ?? 'failed to load tasks')
      setTasks(data.tasks)
      setSource(data.source)
      setError('')
    } catch (err) {
      setError(err instanceof Error ? err.message : String(err))
    }
  }, [])

  useEffect(() => {
    load()
  }, [load])

  async function send(input: RequestInfo, init?: RequestInit) {
    setBusy(true)
    try {
      const res = await fetch(input, init)
      if (!res.ok && res.status !== 204) {
        const data = await res.json().catch(() => ({}))
        throw new Error(data.error ?? `request failed (${res.status})`)
      }
      await load()
    } catch (err) {
      setError(err instanceof Error ? err.message : String(err))
    } finally {
      setBusy(false)
    }
  }

  async function add(event: React.FormEvent) {
    event.preventDefault()
    const value = title.trim()
    if (!value) return
    setTitle('')
    await send('/api/tasks', {
      method: 'POST',
      headers: { 'content-type': 'application/json' },
      body: JSON.stringify({ title: value }),
    })
  }

  return (
    <>
      <section className="panel">
        <form className="row" onSubmit={add}>
          <input
            type="text"
            placeholder="What needs doing?"
            value={title}
            onChange={(e) => setTitle(e.target.value)}
            aria-label="Task title"
          />
          <button type="submit" disabled={busy || !title.trim()}>
            Add task
          </button>
        </form>
        {error && <p className="error">{error}</p>}
      </section>

      <section className="panel">
        {tasks.length === 0 ? (
          <p className="sub" style={{ margin: 0 }}>
            No tasks yet — add the first one above.
          </p>
        ) : (
          <ul className="tasks">
            {tasks.map((task) => (
              <li key={task.id} className={`task ${task.status}`}>
                <span className={`badge ${task.status}`}>{task.status}</span>
                <span className="title">{task.title}</span>
                <button
                  className="ghost"
                  disabled={busy}
                  onClick={() =>
                    send(`/api/tasks/${task.id}`, {
                      method: 'PATCH',
                      headers: { 'content-type': 'application/json' },
                      body: JSON.stringify({ status: NEXT_STATUS[task.status] }),
                    })
                  }
                >
                  → {NEXT_STATUS[task.status]}
                </button>
                <button
                  className="ghost"
                  disabled={busy}
                  onClick={() => send(`/api/tasks/${task.id}`, { method: 'DELETE' })}
                  aria-label={`Delete ${task.title}`}
                >
                  Delete
                </button>
              </li>
            ))}
          </ul>
        )}
        <div className="meta">
          <span>
            {tasks.length} task{tasks.length === 1 ? '' : 's'}
          </span>
          {source && (
            <span>
              · served from <code>{source}</code>
            </span>
          )}
          <button className="ghost" style={{ marginLeft: 'auto' }} onClick={load} disabled={busy}>
            Refresh
          </button>
        </div>
      </section>
    </>
  )
}
