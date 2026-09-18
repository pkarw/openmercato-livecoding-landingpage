import { cn } from '@/lib/utils'

export function OpenMercatoLogo({ className }: { className?: string }) {
  return (
    <span className={cn('flex items-center gap-2.5', className)}>
      <img
        src="/brand/openmercato-mark.svg"
        alt="Open Mercato"
        className="size-8 shrink-0 rounded-[9px]"
        width={32}
        height={32}
      />
      <span className="text-sm font-bold tracking-[-0.01em] whitespace-nowrap">
        Open Mercato <span className="text-primary">Cloud</span>
      </span>
    </span>
  )
}

export function AiTechLeadersLogo({ className }: { className?: string }) {
  return (
    <span className={cn('flex items-center gap-2.5', className)}>
      <span className="flex h-8 shrink-0 items-center overflow-hidden rounded-[9px] bg-black px-2">
        <img
          src="/brand/aitechleaders-logo.png"
          alt="BRAVE"
          className="h-3.5 w-auto"
          width={334}
          height={108}
        />
      </span>
      <span className="text-sm font-bold tracking-[-0.01em] whitespace-nowrap">
        AI Tech <span className="text-brave">Leaders</span>
      </span>
    </span>
  )
}

export function ProductLogo({ product, className }: { product: 'openmercato' | 'aitechleaders'; className?: string }) {
  return product === 'openmercato' ? (
    <OpenMercatoLogo className={className} />
  ) : (
    <AiTechLeadersLogo className={className} />
  )
}
