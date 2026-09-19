import { useEffect, useMemo, useState } from 'react'
import { ArrowRight, Clock, ShieldCheck, Sparkles, Users } from 'lucide-react'
import { AppQrCode } from '@/components/AppQrCode'
import { AiTechLeadersLogo, OpenMercatoLogo } from '@/components/Brand'
import { ClaimForm } from '@/components/ClaimForm'
import { ClaimedCard } from '@/components/ClaimedCard'
import { Countdown, useCountdown } from '@/components/Countdown'
import { OfferPicker } from '@/components/OfferPicker'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Separator } from '@/components/ui/separator'
import { api, type ClaimResponse, type Offer } from '@/lib/api'
import { PRIVACY_URL, toInterest, type ProductKey } from '@/lib/offers'

const FALLBACK_OFFER: Offer = {
  discountPercent: 10,
  endsAt: '2026-09-20T23:59:59+02:00',
  active: true,
  claimed: 0,
  source: 'postgres',
}

export default function App() {
  const [offer, setOffer] = useState<Offer>(FALLBACK_OFFER)
  const [selected, setSelected] = useState<ProductKey[]>(['openmercato', 'aitechleaders'])
  const [claimed, setClaimed] = useState<ClaimResponse | null>(null)

  useEffect(() => {
    api.offer().then(setOffer).catch(() => setOffer(FALLBACK_OFFER))
  }, [])

  const { total } = useCountdown(offer.endsAt)
  const live = offer.active && total > 0
  const interest = useMemo(() => toInterest(selected), [selected])

  const deadline = new Date(offer.endsAt).toLocaleDateString('en-GB', {
    weekday: 'long',
    day: 'numeric',
    month: 'long',
  })
  const shortDeadline = new Date(offer.endsAt).toLocaleDateString('en-GB', {
    day: '2-digit',
    month: '2-digit',
  })

  function toggle(key: ProductKey) {
    setSelected((current) =>
      current.includes(key) ? current.filter((item) => item !== key) : [...current, key],
    )
  }

  return (
    <div className="flex min-h-svh flex-col">
      <header className="sticky top-0 z-30 border-b border-border/70 bg-background/80 backdrop-blur-md">
        <div className="mx-auto flex h-16 w-full max-w-6xl items-center justify-between gap-4 px-4 sm:px-6">
          <div className="flex items-center gap-3 sm:gap-4">
            <OpenMercatoLogo />
            <span className="hidden h-5 w-px bg-border sm:block" />
            <AiTechLeadersLogo className="hidden sm:flex" />
          </div>
          <Button asChild size="sm" className="font-semibold">
            <a href="#claim">
              Claim -{offer.discountPercent}%
              <ArrowRight className="size-4" />
            </a>
          </Button>
        </div>
      </header>

      <main className="flex-1">
        {/* Hero */}
        <section className="glow-grid border-b border-border">
          <div className="mx-auto w-full max-w-6xl px-4 py-16 text-center sm:px-6 sm:py-24">
            <Badge
              variant="outline"
              className="mb-6 max-w-full gap-2 border-primary/40 bg-primary/10 px-3 py-1 text-[10px] font-semibold tracking-[0.16em] whitespace-normal text-primary uppercase sm:text-xs sm:tracking-[0.18em]"
            >
              <Clock className="size-3.5 shrink-0" />
              <span className="hidden sm:inline">Limited — ends {deadline}</span>
              <span className="sm:hidden">Limited — ends Sunday {shortDeadline}</span>
            </Badge>

            <h1 className="mx-auto max-w-4xl text-4xl leading-[1.05] font-extrabold tracking-[-0.03em] text-balance sm:text-6xl">
              Take <span className="text-primary">-{offer.discountPercent}%</span> off Open Mercato Cloud and the
              AI Tech Leaders training
            </h1>

            <p className="hand-note mt-5 text-2xl text-primary/90">one form, one code, your pick</p>

            <p className="mx-auto mt-5 max-w-2xl text-base leading-relaxed text-muted-foreground sm:text-lg">
              Sandboxes where agents build your business app, and the 5-week program that teaches your team the
              process behind them. Pick one or take both — leave your email and we will send your personal code before the window closes.
            </p>

            <div className="mt-10 flex flex-col items-center gap-5">
              <Countdown endsAt={offer.endsAt} />
              <div className="flex flex-wrap items-center justify-center gap-x-5 gap-y-2 text-sm text-muted-foreground">
                <span className="flex items-center gap-2">
                  <Users className="size-4 text-primary" />
                  {offer.claimed} {offer.claimed === 1 ? 'person has' : 'people have'} reserved the discount
                </span>
                <span className="flex items-center gap-2">
                  <ShieldCheck className="size-4 text-primary" />
                  Consent-based, GDPR-friendly
                </span>
              </div>
              <Button asChild size="lg" className="h-12 px-8 text-base font-semibold">
                <a href="#claim">
                  <Sparkles className="size-4" />
                  Reserve my discount
                </a>
              </Button>
              {/* Desktop only: a phone visitor is already on the device they would scan with. */}
              <AppQrCode className="mt-2 hidden md:flex" />
            </div>
          </div>
        </section>

        {/* Pick + claim */}
        <section id="claim" className="mx-auto w-full max-w-6xl scroll-mt-20 px-4 py-16 sm:px-6 sm:py-20">
          <div className="grid gap-10 lg:grid-cols-[minmax(0,1.15fr)_minmax(0,0.85fr)] lg:gap-12">
            <div className="space-y-6">
              <div className="space-y-2">
                <span className="text-xs font-semibold tracking-[0.18em] text-muted-foreground uppercase">
                  Step 1
                </span>
                <h2 className="text-2xl font-bold tracking-tight sm:text-3xl">What is the code for?</h2>
                <p className="text-sm text-muted-foreground">
                  Select one or both. Your choice is saved with the lead, so the code we issue matches it.
                </p>
              </div>

              <OfferPicker selected={selected} onToggle={toggle} discountPercent={offer.discountPercent} />
            </div>

            <div className="lg:sticky lg:top-24 lg:self-start">
              <div className="rounded-2xl border border-border bg-card p-6 shadow-[0_24px_60px_-40px_rgba(0,0,0,0.9)] sm:p-8">
                {claimed ? (
                  <ClaimedCard result={claimed} liveDiscountPercent={offer.discountPercent} />
                ) : live ? (
                  <div className="space-y-6">
                    <div className="space-y-2">
                      <span className="text-xs font-semibold tracking-[0.18em] text-muted-foreground uppercase">
                        Step 2
                      </span>
                      <h2 className="text-2xl font-bold tracking-tight">Where should we send it?</h2>
                      <p className="text-sm text-muted-foreground">
                        {interest
                          ? 'We email your personal code before the offer ends — redeem it when you sign up.'
                          : 'Pick at least one offer on the left to continue.'}
                      </p>
                    </div>

                    <ClaimForm
                      interest={interest}
                      discountPercent={offer.discountPercent}
                      onClaimed={setClaimed}
                    />
                  </div>
                ) : (
                  <div className="space-y-3 py-6 text-center">
                    <h2 className="text-2xl font-bold tracking-tight">The offer has closed</h2>
                    <p className="text-sm text-muted-foreground">
                      The -{offer.discountPercent}% window ended on {deadline}. Both products are still open for
                      business — say hello and we will let you know about the next round.
                    </p>
                    <Button asChild variant="outline">
                      <a href="https://openmercatocloud.com/" target="_blank" rel="noreferrer">
                        Visit openmercatocloud.com
                      </a>
                    </Button>
                  </div>
                )}
              </div>
            </div>
          </div>
        </section>
      </main>

      <footer className="border-t border-border bg-card/40">
        <div className="mx-auto w-full max-w-6xl space-y-6 px-4 py-10 sm:px-6">
          <div className="flex flex-col gap-6 sm:flex-row sm:items-center sm:justify-between">
            <div className="flex flex-wrap items-center gap-6">
              <a href="https://openmercatocloud.com/" target="_blank" rel="noreferrer" className="hover:opacity-80">
                <OpenMercatoLogo />
              </a>
              <a href="https://aitechleaders.pl/" target="_blank" rel="noreferrer" className="hover:opacity-80">
                <AiTechLeadersLogo />
              </a>
            </div>
            <nav className="flex flex-wrap items-center gap-x-6 gap-y-2 text-sm text-muted-foreground">
              <a href="https://openmercatocloud.com/" target="_blank" rel="noreferrer" className="hover:text-foreground">
                openmercatocloud.com
              </a>
              <a href="https://aitechleaders.pl/" target="_blank" rel="noreferrer" className="hover:text-foreground">
                aitechleaders.pl
              </a>
              <a href={PRIVACY_URL} target="_blank" rel="noreferrer" className="hover:text-foreground">
                Privacy policy
              </a>
            </nav>
          </div>

          <Separator />

          <p className="text-xs leading-relaxed text-muted-foreground">
            The -{offer.discountPercent}% discount applies to a first purchase of an Open Mercato Cloud plan or a
            seat in the AI Tech Leaders training and is valid until the end of {deadline}. One discount per email
            address. Your data is processed under the{' '}
            <a href={PRIVACY_URL} target="_blank" rel="noreferrer" className="underline underline-offset-4">
              OpenMercatoCloud.com privacy policy
            </a>{' '}
            and used to send you the code and marketing communications about OpenMercatoCloud.com and
            AiTechLeaders.pl until you withdraw consent.
          </p>
        </div>
      </footer>
    </div>
  )
}
