# Increase the Offer Discount to 15 Percent — Execution Plan

Source doc: `.ai/specs/2026-09-19-increase-offer-discount-to-15-percent.md`

## Goal

Reserve and communicate a stable per-lead discount so new reservations receive 15% while existing 10% reservations keep their original entitlement.

## Scope

- Add a permanent `discount_percent` snapshot to `leads` and carry it through repository reads, claim responses, and both notification paths.
- Explain retained discounts in the claimed UI and align repository-owned offer defaults, metadata, test configuration, and documentation to 15%.
- Add integration coverage using a disposable PostgreSQL database, including existing-lead, new-lead, duplicate, and database-default cases.
- Preserve the current campaign deadline. This implementation follows Q1 option 2 from the source spec: ship the requested 15% behavior without extending the commercial window. Production environment activation and deployment are operator-owned.

## Non-goals

- Changing `OFFER_ENDS_AT`, product eligibility, or the one-reservation-per-email rule.
- Retroactively upgrading existing reservations or removing the permanent 10% database default.
- Merging or duplicating the full security scope of PR #14. Until that dependency lands, the current conflict path must at least leave the stored code and percentage untouched; any later conflict resolution must preserve that invariant.
- Sending real email or reading/modifying production lead data during tests.

## Implementation Plan

### Phase 1: Preserve reservation entitlements

1. Add the additive `discount_percent` migration and the disposable PostgreSQL test harness needed to verify migration/backfill behavior.
2. Extend the lead model and repository so new claims store the active percentage while repeat and concurrent claims return the stored entitlement unchanged; add integration regression coverage.
3. Make the API and notification content use the stored percentage, including fulfillment visibility; add response and rendered-email coverage without sending email.
4. Add claimed-state copy that explains a retained lower percentage and cover both the lower-than-live and matching-offer states.

### Phase 2: Activate and verify 15%

5. Align server/client fallbacks, metadata, environment/test defaults, migration docs, and README copy to 15%, while retaining 10% only where it describes historical reservations or the database default.
6. Run the configured validation gate, the PostgreSQL integration suite, targeted stale-reference checks, and browser QA for new and returning reservation states.

## Risks

- This is a money- and schema-affecting change, so it requires `risk-high`, independent review, negative-path integration coverage, and UI QA.
- PR #14 is still open and changes the same duplicate-claim branch. This branch preserves the discount entitlement on current `main`; if #14 lands first, reconcile its read-only conflict path without restoring the old update behavior.
- The existing deadline remains unchanged and is close. Repository readiness does not prove the production environment has been set to 15% or deployed before the offer closes.

## Progress

> Convention: `- [ ]` pending, `- [x]` done. Append ` — <commit sha>` when a step lands. Do not rename step titles.

### Phase 1: Preserve reservation entitlements

- [x] 1.1 Add the discount snapshot migration and disposable PostgreSQL test harness — 47579b5
- [ ] 1.2 Store and preserve per-lead discount entitlements with integration coverage
- [ ] 1.3 Use stored discounts in API and notification output with regression coverage
- [ ] 1.4 Explain retained discounts in the claimed UI with state coverage

### Phase 2: Activate and verify 15%

- [ ] 2.1 Align repository-owned offer defaults, metadata, test configuration, and documentation to 15%
- [ ] 2.2 Run the full validation, integration, stale-reference, review, and UI verification gates
