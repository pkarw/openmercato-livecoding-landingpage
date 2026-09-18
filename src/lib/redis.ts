import Redis from 'ioredis'

const globalForRedis = globalThis as unknown as { redis?: Redis }

export const redis =
  globalForRedis.redis ??
  new Redis(process.env.REDIS_URL ?? 'redis://127.0.0.1:6379/0', {
    maxRetriesPerRequest: 2,
    lazyConnect: false,
  })

// Without a handler ioredis throws an unhandled error event when the cache is down;
// the app keeps working straight off Postgres instead.
redis.on('error', (err) => console.warn('[redis]', err.message))

if (process.env.NODE_ENV !== 'production') globalForRedis.redis = redis

const TTL = Number(process.env.CACHE_TTL_SECONDS ?? 30)

export const TASKS_CACHE_KEY = 'tasks:list'

export async function readCache<T>(key: string): Promise<T | null> {
  try {
    const raw = await redis.get(key)
    return raw ? (JSON.parse(raw) as T) : null
  } catch {
    return null
  }
}

export async function writeCache(key: string, value: unknown): Promise<void> {
  try {
    await redis.set(key, JSON.stringify(value), 'EX', TTL)
  } catch {
    /* cache is best-effort */
  }
}

export async function dropCache(key: string): Promise<void> {
  try {
    await redis.del(key)
  } catch {
    /* cache is best-effort */
  }
}
