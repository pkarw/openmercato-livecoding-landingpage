# Increase the Offer Discount to 15 Percent — Execution Plan

Source doc: `.ai/specs/2026-09-19-increase-offer-discount-to-15-percent.md`

## Goal

Reserve and communicate a stable per-lead discount so new reservations receive 15% while existing 10% reservations keep their original entitlement.

## Scope

- Add a permanent `discount_percent` snapshot to `leads` and carry it through repository reads, durable notification payloads, and both email paths.
- Keep the anonymous claim response privacy-safe while aligning repository-owned offer defaults, metadata, test configuration, and documentation to 15%.
- Add integration coverage using a disposable PostgreSQL database, including existing-lead, new-lead, duplicate, and database-default cases.
- Preserve the current campaign deadline. This implementation follows Q1 option 2 from the source spec: ship the requested 15% behavior without extending the commercial window. Production environment activation and deployment are operator-owned.

## Non-goals

- Changing `OFFER_ENDS_AT`, product eligibility, or the one-reservation-per-email rule.
- Retroactively upgrading existing reservations or removing the permanent 10% database default.
- Weakening the anonymous-response privacy contract from PR #14; stored entitlement details remain mailbox-only.
- Sending real email or reading/modifying production lead data during tests.

## Implementation Plan

### Phase 1: Preserve reservation entitlements

1. Add the additive `discount_percent` migration and the disposable PostgreSQL test harness needed to verify migration/backfill behavior.
2. Extend the lead model and repository so new claims store the active percentage while repeat and concurrent claims return the stored entitlement unchanged; add integration regression coverage.
3. Snapshot the stored percentage into the durable notification outbox and use it for customer and fulfillment email content; add rendered-message coverage without sending email.
4. Preserve the privacy-safe generic claimed state so anonymous callers cannot discover a stored entitlement.

### Phase 2: Activate and verify 15%

5. Align server/client fallbacks, metadata, environment/test defaults, migration docs, and README copy to 15%, while retaining 10% only where it describes historical reservations or the database default.
6. Run the configured validation gate, the PostgreSQL integration suite, targeted stale-reference checks, and browser QA for new and returning reservation states.

## Risks

- This is a money- and schema-affecting change, so it requires `risk-high`, independent review, negative-path integration coverage, and UI QA.
- Current `main` includes the anonymous-claim privacy and durable notification-outbox changes. Conflict resolution must preserve both while snapshotting the stored entitlement into queued deliveries.
- The existing deadline remains unchanged and is close. Repository readiness does not prove the production environment has been set to 15% or deployed before the offer closes.

## Progress

PR: #24

> Convention: `- [ ]` pending, `- [x]` done. Append ` — <commit sha>` when a step lands. Do not rename step titles.

### Phase 1: Preserve reservation entitlements

- [x] 1.1 Add the discount snapshot migration and disposable PostgreSQL test harness — 47579b5
- [x] 1.2 Store and preserve per-lead discount entitlements with integration coverage — 7d9666d
- [x] 1.3 Use stored discounts in API and notification output with regression coverage — 96a60b5
- [x] 1.4 Explain retained discounts in the claimed UI with state coverage — 2422320

### Phase 2: Activate and verify 15%

- [x] 2.1 Align repository-owned offer defaults, metadata, test configuration, and documentation to 15% — 18a13c5
- [x] 2.2 Run the full validation, integration, stale-reference, review, and UI verification gates — 6ea4e27
