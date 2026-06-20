# Frontend audit — deferred follow-ups

A multi-dimension adversarial audit (security · GDPR/legal · production-readiness · a11y · architecture · i18n/DX)
produced **50 verified findings**. The frontend-only, clear-cut ones were fixed (see git history). The items below need a
**backend change** or a **product/ops decision**, so they are documented here rather than silently half-implemented.

## Needs a backend change

| # | Finding | Why it needs backend | Suggested change |
|---|---|---|---|
| 1 | **Terms acceptance is not server-recorded** | `acceptTerms` is validated client-side only; `RegisterUserCommand` has no field for it. GDPR Art. 7(1) wants provable consent. | Add `AcceptedTermsVersion` (string) to `RegisterUserRequest`/`Command`, persist on the `User` (or an audit row) with the policy version + timestamp. FE then sends the current terms version. |
| 2 | **Consent records lack versioning** | `ConsentRecord {UserId, ConsentType, Granted, RecordedAt}` — no `PolicyVersion`/`DocumentHash`. Can't prove *what* was consented to. | Add `PolicyVersion` (nullable, ≤32) to `ConsentRecord` + `GrantConsentCommand`; FE passes the active version. |
| 3 | **Marketing consent not enforced for the welcome e-mail** | `SendWelcomeHandler` sends on registration regardless of the `marketing_emails` consent (which starts unset). | Either treat the welcome e-mail as **transactional** (and relabel the UI toggle so it clearly excludes transactional mail), or gate non-transactional sends on a granted `marketing_emails` consent. |
| 4 | **SSE realtime is wired for notifications only** | The backend publishes exactly one realtime event type (`"notification"`). Credit balance / subscription / operation changes are not pushed (operations relies on client polling). | If live billing/credit updates are desired, publish `billing.credits_changed` / `billing.subscription_changed` from the relevant handlers — the FE `event-map.ts` already has the (currently dormant) entries, so it "just works" once the backend emits them. |

## Needs a product / ops decision

| # | Finding | Decision needed |
|---|---|---|
| 5 | **Orphaned consent types** (`analytics`, `third_party_sharing`) | The UI offers these toggles but nothing in the app actually uses analytics or third-party sharing yet. Decide: remove the toggles until the functionality exists, or keep them as forward-looking preferences. |
| 6 | **SESSION_PASSWORD rotation** | `iron-session` supports a keyed password map (`{1: old, 2: new}`) for zero-downtime secret rotation. Adopt it (+ a `SESSION_PASSWORD_ID`) when a rotation policy is defined. |
| 7 | **Single-flight refresh is per-process** | The in-flight refresh coalescing in `lib/server/backend.ts` is a module-level `Map`. On a multi-instance Next deployment two instances can refresh the same one-time-use token concurrently (reuse-detection could kill the session). For multi-node, back it with a shared lock (e.g. Redis). Single-instance deploys are fine today. |
| 8 | **Client error reporting / observability** | `instrumentation.ts` now logs server-side request errors, but there is no client SDK (Sentry/DataDog) and no client `window.onerror`/unhandledrejection reporting. Pick a provider before GA if you want production error visibility. |
| 9 | **Full i18n of feature UI** | Only nav labels + error codes are localized; feature UI strings are inline EN by design. The locale toggle therefore only changes nav. Decide whether to translate feature UI (move strings into next-intl messages) or scope the toggle's promise. |
| 10 | **Platform-admin pages** | `/platform/users` and `/platform/audit` were removed from the platform nav (no pages existed). Build them (platform-level user/audit views) if the platform-admin surface needs them; identity admin already exists per-tenant at `/admin`. |

*Generated from the 2026 frontend gap audit. Fixed items are in the commit "Address audit gaps …".*
