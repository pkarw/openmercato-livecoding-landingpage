import { useEffect, useState } from 'react'
import { cn } from '@/lib/utils'

type Remaining = { days: number; hours: number; minutes: number; seconds: number; total: number }

function remainingUntil(endsAt: string): Remaining {
  const total = Math.max(0, new Date(endsAt).getTime() - Date.now())
  const seconds = Math.floor(total / 1000)
  return {
    days: Math.floor(seconds / 86_400),
    hours: Math.floor((seconds % 86_400) / 3_600),
    minutes: Math.floor((seconds % 3_600) / 60),
    seconds: seconds % 60,
    total,
  }
}

export function useCountdown(endsAt: string): Remaining {
  const [remaining, setRemaining] = useState(() => remainingUntil(endsAt))

  useEffect(() => {
    setRemaining(remainingUntil(endsAt))
    const timer = window.setInterval(() => setRemaining(remainingUntil(endsAt)), 1_000)
    return () => window.clearInterval(timer)
  }, [endsAt])

  return remaining
}

export function Countdown({ endsAt, className }: { endsAt: string; className?: string }) {
  const { days, hours, minutes, seconds, total } = useCountdown(endsAt)

  if (total === 0) {
    return (
      <p className={cn('text-sm font-medium text-muted-foreground', className)}>
        This round of the offer has closed.
      </p>
    )
  }

  const cells = [
    { value: days, label: days === 1 ? 'day' : 'days' },
    { value: hours, label: 'hours' },
    { value: minutes, label: 'minutes' },
    { value: seconds, label: 'seconds' },
  ]

  return (
    <div className={cn('flex items-stretch gap-2 sm:gap-3', className)} aria-label="Time left to claim the discount">
      {cells.map((cell) => (
        <div
          key={cell.label}
          className="flex min-w-[4.5rem] flex-col items-center rounded-xl border border-border bg-card/80 px-3 py-2.5 backdrop-blur-sm sm:min-w-20"
        >
          <span className="font-sans text-2xl font-bold tabular-nums sm:text-3xl">
            {String(cell.value).padStart(2, '0')}
          </span>
          <span className="text-[10px] font-semibold tracking-[0.18em] text-muted-foreground uppercase">
            {cell.label}
          </span>
        </div>
      ))}
    </div>
  )
}
