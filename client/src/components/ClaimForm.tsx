import { useState, type FormEvent } from 'react'
import { AlertCircle, ArrowRight, Loader2 } from 'lucide-react'
import { Button } from '@/components/ui/button'
import { Checkbox } from '@/components/ui/checkbox'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import { api, type ClaimResponse, type Interest } from '@/lib/api'
import { PRIVACY_URL } from '@/lib/offers'

type Props = {
  interest: Interest | null
  discountPercent: number
  onClaimed: (result: ClaimResponse) => void
}

export function ClaimForm({ interest, discountPercent, onClaimed }: Props) {
  const [email, setEmail] = useState('')
  const [name, setName] = useState('')
  const [privacyAccepted, setPrivacyAccepted] = useState(false)
  const [marketingConsent, setMarketingConsent] = useState(false)
  const [error, setError] = useState('')
  const [pending, setPending] = useState(false)

  const ready = Boolean(interest) && email.trim().length > 3 && privacyAccepted && marketingConsent

  async function submit(event: FormEvent) {
    event.preventDefault()
    if (!interest) {
      setError('Pick Open Mercato Cloud, the AI Tech Leaders training, or both.')
      return
    }

    setPending(true)
    setError('')
    try {
      const result = await api.claim({
        email: email.trim(),
        name: name.trim() || undefined,
        interest,
        privacyAccepted,
        marketingConsent,
        source: window.location.pathname + window.location.search,
      })
      onClaimed(result)
    } catch (err) {
      setError(err instanceof Error ? err.message : String(err))
    } finally {
      setPending(false)
    }
  }

  return (
    <form onSubmit={submit} className="space-y-5" noValidate>
      <div className="grid gap-4 sm:grid-cols-2">
        <div className="space-y-2">
          <Label htmlFor="name">Name</Label>
          <Input
            id="name"
            name="name"
            autoComplete="name"
            placeholder="Optional"
            value={name}
            onChange={(event) => setName(event.target.value)}
            className="h-11"
          />
        </div>
        <div className="space-y-2">
          <Label htmlFor="email">
            Work email <span className="text-primary">*</span>
          </Label>
          <Input
            id="email"
            name="email"
            type="email"
            required
            autoComplete="email"
            placeholder="you@company.com"
            value={email}
            onChange={(event) => setEmail(event.target.value)}
            className="h-11"
          />
        </div>
      </div>

      <div className="space-y-3 rounded-xl border border-border bg-background/40 p-4">
        <label className="flex cursor-pointer items-start gap-3 text-sm leading-relaxed">
          <Checkbox
            checked={privacyAccepted}
            onCheckedChange={(checked) => setPrivacyAccepted(checked === true)}
            className="mt-0.5"
            aria-label="Accept the privacy policy"
          />
          <span className="text-muted-foreground">
            I accept the{' '}
            <a
              href={PRIVACY_URL}
              target="_blank"
              rel="noreferrer"
              className="font-medium text-foreground underline underline-offset-4 hover:text-primary"
            >
              privacy policy
            </a>{' '}
            of OpenMercatoCloud.com. <span className="text-primary">*</span>
          </span>
        </label>

        <label className="flex cursor-pointer items-start gap-3 text-sm leading-relaxed">
          <Checkbox
            checked={marketingConsent}
            onCheckedChange={(checked) => setMarketingConsent(checked === true)}
            className="mt-0.5"
            aria-label="Agree to marketing communications"
          />
          <span className="text-muted-foreground">
            I agree to receive marketing communications about OpenMercatoCloud.com and AiTechLeaders.pl,
            including this discount code by email. I can withdraw consent at any time.{' '}
            <span className="text-primary">*</span>
          </span>
        </label>
      </div>

      {error && (
        <p className="flex items-start gap-2 text-sm text-destructive" role="alert">
          <AlertCircle className="mt-0.5 size-4 shrink-0" />
          {error}
        </p>
      )}

      <Button type="submit" size="lg" disabled={!ready || pending} className="h-12 w-full text-base font-semibold">
        {pending ? <Loader2 className="size-4 animate-spin" /> : null}
        Reserve my {discountPercent}% discount
        {!pending && <ArrowRight className="size-4" />}
      </Button>

      <p className="text-center text-xs text-muted-foreground">
        One discount per email address. No spam — unsubscribe with a single reply.
      </p>
    </form>
  )
}
