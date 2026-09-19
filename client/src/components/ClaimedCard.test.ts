import { describe, expect, it } from 'vitest'
import type { ClaimResponse } from '@/lib/api'
import { retainedDiscountMessage } from './ClaimedCard'

function result(discountPercent: number, alreadyClaimed = true): ClaimResponse {
  return {
    lead: {
      email: 'lead@example.test',
      name: null,
      interest: 'openmercato',
      createdAt: '2026-09-19T00:00:00Z',
    },
    discountPercent,
    endsAt: '2026-09-20T21:59:59Z',
    alreadyClaimed,
  }
}

describe('retainedDiscountMessage', () => {
  it('explains a returning reservation below the live offer', () => {
    expect(retainedDiscountMessage(result(10), 15)).toBe(
      'You reserved your 10% earlier and that code stays valid.',
    )
  })

  it('stays silent when the stored and live percentages match', () => {
    expect(retainedDiscountMessage(result(15), 15)).toBeNull()
  })

  it('stays silent for a new reservation', () => {
    expect(retainedDiscountMessage(result(10, false), 15)).toBeNull()
  })
})
