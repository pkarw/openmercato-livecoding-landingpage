# Execution plan — scannable QR code on the home page for the app URL

Source doc: `.ai/specs/2026-09-19-home-page-qr-code.md` (spec PR #22, design-only)
Issue: #19
Engine: om-auto-create-pr (steps: 3, --loop: no)

## Goal

Put a QR code in the hero of the landing page that encodes the deployment URL, so an audience reading the page on a projector or someone else's screen can open it on their own phone instead of retyping 78 characters of UUID hostname.

## Scope

Frontend only, inside `client/`:

- `APP_URL` constant in `client/src/lib/offers.ts`, beside the existing `PRIVACY_URL`.
- New presentational component `client/src/components/AppQrCode.tsx`.
- One placement in the hero of `client/src/App.tsx`, gated `hidden md:flex`.
- One new runtime dependency, `qrcode.react@^4.2.0`, plus the resulting `client/package-lock.json` change.

## Non-goals

- No `server/` or `/api` change; no schema, migration, or stored data.
- No scan tracking, analytics, or UTM tagging of the QR payload (issue #19 names it a non-goal; the spec records it as the obvious follow-up).
- No dynamic, per-visitor, or short-link QR codes.
- No change to the offer, claim, or email flows.
- No third-party QR image service — the code is rendered client-side, so a flaky conference network cannot break it.
- No bootstrapping of a test framework (see Risks).

## Implementation Plan

### Phase 1: Scannable QR in the hero

**Step 1.1 — Add the renderer dependency.** Add `qrcode.react@^4.2.0` to `client/package.json` dependencies and update `client/package-lock.json`. Verified by: `npm --prefix client run typecheck` and `npm --prefix client run build` both pass; the lockfile records 4.2.x with no new transitive runtime dependency. The app is unchanged and working.

**Step 1.2 — Add `APP_URL` and the `AppQrCode` component.** Export `APP_URL` from `client/src/lib/offers.ts` with a comment marking it deployment-specific. Create `client/src/components/AppQrCode.tsx`: `QRCodeSVG` with `size={176}`, `marginSize={4}`, `level="M"`, `fgColor="#141313"`, `bgColor="#ffffff"` on an explicitly white padded tile inside a `rounded-2xl border border-border bg-card` card, with a short label, `role="img"` + `aria-label`, and an `<a href={APP_URL}>` beside it. Accept an optional `className` through `cn()`, matching `Countdown`. Verified by: `npm --prefix client run typecheck` passes. The component is not yet mounted, so the app builds and runs exactly as before.

**Step 1.3 — Mount it in the hero and verify.** Render `<AppQrCode className="hidden md:flex" />` in `client/src/App.tsx`, in the hero's `mt-10 flex flex-col items-center gap-5` block after `<Countdown />`. Verified by: the full validation gate; a screenshot of the hero at ≥ `md` width showing a dark-on-light code and the link; a screenshot below `md` showing it absent; and a check that no QR-related request is made at page load.

## Risks

- **Merge gate — ⚠ NEEDS HUMAN CONFIRMATION.** The spec's Q4 ("who is the scanner?") is an assumption, not a decision: issue #19 raised it itself because the originating brief never named the audience. The design assumes a person reading *this page* on a screen they cannot type into. If the intent is a **printed** slide or leaflet, then hardcoding the deployment URL gets worse and a `hidden md:flex` hero block is the wrong home. This PR therefore stays a **draft**.
- **Hardcoded deployment host.** A redeploy under a new app id points both the QR and the link at a dead address, silently. Containment, not detection: one constant, one comment.
- **New runtime dependency.** `qrcode.react` 4.2.0 — ISC, zero transitive runtime deps, React 19 peer range, inline SVG output. The only widening of project surface here; reversible in one commit.
- **No automated regression protects this component.** `SDLC.md` § Validation records that this repository has no test suite and scopes its mandatory-regression-test rule to security fixes. Bootstrapping a test framework (vitest + Testing Library + config + scripts) would be a larger and less reversible change than the feature itself, and was deliberately not pulled into this PR. Verification is therefore the configured gate, visual evidence from UI QA, and one manual phone scan. **Flagged for the reviewer:** if the project wants a test harness, that is its own ticket.
- **`hidden md:flex` means a phone-width screenshot contains no code** — a partial miss against one trigger named in the spec's TLDR. Accepted; relaxing the gate would crowd the mobile hero, which issue #19 rules out.

## Progress

PR: #23

> Convention: `- [ ]` pending, `- [x]` done. Append ` — <commit sha>` when a step lands. Do not rename step titles.

### Phase 1: Scannable QR in the hero

- [x] 1.1 Add the renderer dependency — 999c9b8
- [x] 1.2 Add `APP_URL` and the `AppQrCode` component — 6a017da
- [x] 1.3 Mount it in the hero and verify — c761638 (review fixes: 6d937d3)
