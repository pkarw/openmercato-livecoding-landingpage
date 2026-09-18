import { Check } from 'lucide-react'
import { ProductLogo } from '@/components/Brand'
import { Badge } from '@/components/ui/badge'
import { PRODUCTS, discounted, type ProductKey } from '@/lib/offers'
import { cn } from '@/lib/utils'

type Props = {
  selected: ProductKey[]
  onToggle: (key: ProductKey) => void
  discountPercent: number
}

export function OfferPicker({ selected, onToggle, discountPercent }: Props) {
  return (
    <div className="grid gap-4 md:grid-cols-2">
      {PRODUCTS.map((product) => {
        const isSelected = selected.includes(product.key)

        return (
          <button
            key={product.key}
            type="button"
            onClick={() => onToggle(product.key)}
            aria-pressed={isSelected}
            className={cn(
              'group relative flex h-full flex-col gap-5 rounded-2xl border p-6 text-left transition-all',
              'focus-visible:ring-[3px] focus-visible:ring-ring/50 focus-visible:outline-none',
              isSelected
                ? 'border-primary bg-card shadow-[0_0_0_1px_var(--primary),0_18px_40px_-24px_var(--primary)]'
                : 'border-border bg-card/60 hover:border-muted-foreground/40 hover:bg-card',
            )}
          >
            <span className="flex items-start justify-between gap-4">
              <ProductLogo product={product.key} />
              <span
                className={cn(
                  'flex size-6 shrink-0 items-center justify-center rounded-full border transition-colors',
                  isSelected
                    ? 'border-primary bg-primary text-primary-foreground'
                    : 'border-border text-transparent group-hover:border-muted-foreground/50',
                )}
                aria-hidden="true"
              >
                <Check className="size-3.5" strokeWidth={3} />
              </span>
            </span>

            <span className="space-y-2">
              <span className="block text-xs font-semibold tracking-[0.18em] text-muted-foreground uppercase">
                {product.tagline}
              </span>
              <span className="block text-sm leading-relaxed text-muted-foreground">{product.description}</span>
            </span>

            <ul className="space-y-2 text-sm">
              {product.highlights.map((highlight) => (
                <li key={highlight} className="flex items-start gap-2.5">
                  <Check className="mt-0.5 size-4 shrink-0 text-primary" strokeWidth={2.5} />
                  <span className="text-foreground/90">{highlight}</span>
                </li>
              ))}
            </ul>

            <span className="mt-auto flex flex-wrap items-baseline gap-x-3 gap-y-1 border-t border-border pt-4">
              <span className="text-sm text-muted-foreground line-through">{product.listPrice}</span>
              <span className="text-xl font-bold">{discounted(product, discountPercent)}</span>
              <Badge className="bg-primary text-primary-foreground">-{discountPercent}%</Badge>
              <span className="w-full text-xs text-muted-foreground">{product.priceNote}</span>
            </span>
          </button>
        )
      })}
    </div>
  )
}
