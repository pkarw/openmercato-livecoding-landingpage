import { renderToStaticMarkup } from 'react-dom/server'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { loadOffer, OfferPage, OfferStatus, type OfferState } from '@/App'
import type { Offer } from '@/lib/api'

const activeOffer: Offer = {
  discountPercent: 17,
  endsAt: '2026-09-20T21:59:59Z',
  active: true,
  claimed: 41,
  source: 'postgres',
}

afterEach(() => {
  vi.useRealTimers()
})

describe('offer loading', () => {
  it('keeps campaign terms unavailable while the request is pending', async () => {
    let resolveOffer!: (offer: Offer) => void
    const request = () => new Promise<Offer>((resolve) => (resolveOffer = resolve))
    const states: OfferState[] = []

    const pending = loadOffer(request, (state) => states.push(state))

    expect(states).toEqual([{ status: 'loading' }])
    expect(renderToStaticMarkup(<OfferStatus state={{ status: 'loading' }} onRetry={() => undefined} />)).not.toContain(
      'Reserve my discount',
    )

    resolveOffer(activeOffer)
    await pending
    expect(states).toEqual([{ status: 'loading' }, { status: 'loaded', offer: activeOffer }])
  })

  it('shows a failure without fabricated terms and accepts a successful retry', async () => {
    const states: OfferState[] = []

    await loadOffer(() => Promise.reject(new Error('offline')), (state) => states.push(state))

    expect(states).toEqual([{ status: 'loading' }, { status: 'failed' }])
    const html = renderToStaticMarkup(<OfferStatus state={{ status: 'failed' }} onRetry={() => undefined} />)
    expect(html).toContain('We could not load the offer')
    expect(html).toContain('Try again')
    expect(html).not.toContain('10%')
    expect(html).not.toContain('Reserve my discount')

    await loadOffer(() => Promise.resolve(activeOffer), (state) => states.push(state))
    expect(states).toEqual([
      { status: 'loading' },
      { status: 'failed' },
      { status: 'loading' },
      { status: 'loaded', offer: activeOffer },
    ])
  })
})

describe('authoritative offer rendering', () => {
  it('renders the discount, deadline, and claim count returned by the server', () => {
    vi.useFakeTimers()
    vi.setSystemTime(new Date('2026-09-19T12:00:00Z'))

    const html = renderToStaticMarkup(<OfferPage offer={activeOffer} />)

    expect(html).toContain('-17%')
    expect(html).toContain('41 people have reserved the discount')
    expect(html).toContain('Reserve my discount')
    expect(html).not.toContain('-10%')
  })

  it('keeps the claim flow unavailable for an expired offer', () => {
    vi.useFakeTimers()
    vi.setSystemTime(new Date('2026-09-21T12:00:00Z'))

    const html = renderToStaticMarkup(<OfferPage offer={activeOffer} />)

    expect(html).toContain('The offer has closed')
    expect(html).not.toContain('<form')
    expect(html).not.toContain('Reserve my discount')
  })
})
