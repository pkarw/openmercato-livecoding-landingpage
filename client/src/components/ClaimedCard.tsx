import { ArrowUpRight, Check, MailCheck } from 'lucide-react'
import { Separator } from '@/components/ui/separator'
import type { ClaimResponse } from '@/lib/api'
import { PRODUCTS } from '@/lib/offers'

export function ClaimedCard({ result }: { result: ClaimResponse }) {
  const { discountPercent } = result

  const steps = [
    'We email your personal discount code before the offer window closes.',
    'Sign up on the site you picked using this same email address.',
    `Enter the code at checkout to take ${discountPercent}% off your first purchase.`,
  ]

  return (
    <div className="space-y-6 text-center">
      <div className="space-y-2">
        <span className="mx-auto flex size-12 items-center justify-center rounded-full bg-primary/15 text-primary">
          <MailCheck className="size-6" />
        </span>
        <h3 className="text-2xl font-bold tracking-tight">Check your inbox</h3>
        <p className="text-sm text-muted-foreground">
          If the address is eligible, we will send the {discountPercent}% discount details by
          email.
        </p>
      </div>

      <ul className="space-y-3 rounded-2xl border border-primary/30 bg-primary/5 px-5 py-5 text-left">
        {steps.map((step) => (
          <li key={step} className="flex items-start gap-3 text-sm">
            <Check className="mt-0.5 size-4 shrink-0 text-primary" strokeWidth={2.5} />
            <span className="text-foreground/90">{step}</span>
          </li>
        ))}
      </ul>

      <Separator />

      <div className="space-y-3 text-left">
        <p className="text-sm font-medium">Have a look around in the meantime:</p>
        {PRODUCTS.map((product) => (
          <a
            key={product.key}
            href={product.url}
            target="_blank"
            rel="noreferrer"
            className="flex items-center justify-between gap-4 rounded-xl border border-border bg-card/60 px-4 py-3 transition-colors hover:border-primary/50 hover:bg-card"
          >
            <span>
              <span className="block text-sm font-semibold">{product.name}</span>
              <span className="block text-xs text-muted-foreground">{product.site}</span>
            </span>
            <ArrowUpRight className="size-4 shrink-0 text-muted-foreground" />
          </a>
        ))}
      </div>
    </div>
  )
}
