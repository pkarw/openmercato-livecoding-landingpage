export type Interest = 'openmercato' | 'aitechleaders' | 'both'

export type Offer = {
  discountPercent: number
  endsAt: string
  active: boolean
  claimed: number
  source: 'postgres' | 'redis'
}

export type ClaimRequest = {
  email: string
  name?: string
  interest: Interest
  privacyAccepted: boolean
  marketingConsent: boolean
  source?: string
}

export type ClaimResponse = {
  /** The discount code stays server-side — it is emailed out separately. */
  lead: {
    email: string
    name: string | null
    interest: Interest
    createdAt: string
  }
  discountPercent: number
  endsAt: string
  alreadyClaimed: boolean
}

async function request<T>(input: string, init?: RequestInit): Promise<T> {
  const response = await fetch(input, {
    ...init,
    headers: init?.body ? { 'content-type': 'application/json', ...init.headers } : init?.headers,
  })

  const payload = await response.json().catch(() => null)

  if (!response.ok) {
    const message =
      payload && typeof payload === 'object' && 'error' in payload
        ? String((payload as { error: unknown }).error)
        : `Something went wrong (${response.status}). Please try again.`
    throw new Error(message)
  }

  return payload as T
}

export const api = {
  offer: () => request<Offer>('/api/offer'),
  claim: (body: ClaimRequest) =>
    request<ClaimResponse>('/api/leads', { method: 'POST', body: JSON.stringify(body) }),
}
