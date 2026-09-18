# Delivery and issue classification

This ASP.NET Core 10 and React landing page uses GitHub issues and PRs against `main`. Tracker operations are defined in `.ai/trackers/github.md`; validation is configured in `.ai/agentic.config.json`.

## Intake

Issues include the affected user, verified evidence and its limits, expected outcome, non-goals, implementation guidance, acceptance criteria, and classified open questions. Security findings must never include credential values or real lead data. Issue preparation does not implement fixes.

## Classification

Apply one category (security or bug), one priority, and one risk. Missing labels are skipped through the tracker existence guards. Priority is urgency; risk is the blast radius of the eventual fix.

When no priority label is set:

- priority-extreme: production outage, data loss, or an active security incident.
- priority-high: security hardening or a release-blocking regression.
- priority-medium: ordinary bug fixes and small improvements.
- priority-low: cosmetic, documentation, or follow-up cleanup.

When no risk label is set:

- risk-high: authentication, data scoping, money, schema migrations, public API changes, or broad changes.
- risk-medium: an ordinary single-area change with tests.
- risk-low: documentation, tests, or isolated cosmetic changes.

## Delivery

Triage and deduplicate before starting. Claim implementation work using assignee, in-progress label when available, and a claim comment; respect others' claims. Do not claim deferred issues. Implement and test in an isolated branch, open a PR, obtain review, and complete QA before merge. A needs-qa PR requires qa-approved. High-risk authorization, data, and money changes require independent review and negative-path integration coverage. PR workflow labels are not applied to deferred issues.

## Validation

- `npm --prefix client run typecheck`
- `npm --prefix client run build`
- `bash scripts/dotnet.sh build server --no-restore`

No repository test suite was present during setup. New security fixes must supply regression tests for their reported scenarios. Do not use production credentials, send real email, or modify real lead data in tests.
