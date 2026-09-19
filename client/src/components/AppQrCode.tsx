import { QRCodeSVG } from 'qrcode.react'
import { APP_URL } from '@/lib/offers'
import { cn } from '@/lib/utils'

const APP_HOST = new URL(APP_URL).host

/**
 * The page is dark-only (`<html class="dark">` in index.html), so the tile the code
 * sits on is explicitly white rather than a theme token: many phone scanners assume a
 * dark-on-light symbol and reject an inverted one. The link below it is the real
 * fallback — nobody has to own a camera to get to the URL.
 *
 * The caller owns the display class (the hero passes `hidden md:flex`), so nothing
 * here sets `flex` itself.
 */
export function AppQrCode({ className }: { className?: string }) {
  return (
    <div
      className={cn(
        'flex-col items-center gap-3 rounded-2xl border border-border bg-card p-5 shadow-[0_24px_60px_-40px_rgba(0,0,0,0.9)]',
        className,
      )}
    >
      <div className="rounded-xl bg-white p-3">
        <QRCodeSVG
          value={APP_URL}
          size={176}
          marginSize={4}
          level="M"
          bgColor="#ffffff"
          fgColor="#141313"
          role="img"
          aria-label={`QR code linking to ${APP_HOST}`}
        />
      </div>
      <p className="text-[10px] font-semibold tracking-[0.18em] text-muted-foreground uppercase">
        Scan to open on your phone
      </p>
      <a
        href={APP_URL}
        className="max-w-[13rem] text-xs break-all text-muted-foreground underline underline-offset-4 hover:text-foreground"
      >
        {APP_HOST}
      </a>
    </div>
  )
}
