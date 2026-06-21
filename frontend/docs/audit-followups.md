# Frontend audit — follow-ups

A multi-dimension adversarial audit (security · GDPR/legal · production-readiness · a11y · architecture · i18n/DX)
produced **50 verified findings**. The frontend-only, clear-cut ones were fixed earlier (commit "Address audit gaps …").
This file tracked the 10 that needed a **backend change** or a **product/ops decision**.

**2026-06-20 update:** 9 of the 10 were implemented (backend + FE, build/tsc/lint clean, 118 backend integration tests +
9 ArchUnit boundary tests green). Only #5 stays deferred (a deliberate product decision). Details below.

## Done (2026-06-20)

| # | Finding | What shipped |
|---|---|---|
| 1 | **Terms acceptance now server-recorded** | `AcceptedTermsVersion` (varchar 32, **not** PII) + `AcceptedTermsAt` (UTC) on `User`; `RegisterUser` command/request/validator/handler/endpoint accept an optional version; migration `IdentityAcceptedTermsVersion`. FE sends `TERMS_VERSION` (`lib/legal-versions.ts`, ISO `2026-06-20`, sourced from the terms page date) on register. |
| 2 | **Consent records versioned** | `PolicyVersion` (nullable varchar 32) on `ConsentRecord`; threaded through `GrantConsent` **and** `WithdrawConsent` (command/request/validator/handler/endpoint) and surfaced in `GetConsents` + the GDPR export; migration `GdprConsentPolicyVersion`. FE sends `PRIVACY_VERSION`. Old rows stay `NULL` (additive). |
| 3 | **Welcome e-mail = transactional** | Decision: the welcome e-mail is transactional → **no** consent gate (no backend change). The UI marketing toggle was relabeled **"Product news & offers"** with a description that makes clear essential/transactional mail (account, security, welcome) is always sent. |
| 4 | **SSE realtime for billing/credit** | Backend now publishes `billing.credits_changed` after commit from every user-initiated balance mutation (top-up, reserve, confirm-spend, release-hold) and `billing.subscription_changed` from `UpsertSubscriptionFromStripe` (activation/cancellation). Post-commit only; lost-race / idempotent paths emit nothing. FE `event-map.ts` already invalidates on those names — live, no polling. **Caveat:** the background `ExpireCredits` CRON does **not** push (no `IRealtimePublisher` in the Jobs host) — a balance drop from expiry reflects on the next fetch, not live. |
| 6 | **SESSION_PASSWORD rotation** | `lib/config.ts` accepts a bare string **or** a JSON map `{"1":"old","2":"new"}` + `SESSION_PASSWORD_ID`. iron-session v8 seals with the highest-keyed entry and unseals with any → zero-downtime rotation. Min-length fail-fast preserved per entry. |
| 7 | **Single-flight refresh, multi-node safe** | When `REDIS_URL` is set, refresh coalescing uses a Redis `SET NX EX` lock keyed on `sha256(refreshToken)`; the winner publishes the rotated token pair, losers read it instead of re-consuming the one-time token (reuse-detection safe). **Without** `REDIS_URL` the behavior is byte-for-byte the old in-proc `Map` (single-node). Any Redis error degrades to local refresh. Added `ioredis`. |
| 8 | **Client error reporting** | `POST /api/log` (no auth, cheap, oversize-guarded) + a `client-error-reporter` mounted in providers attaches `window.onerror` + `unhandledrejection` (sendBeacon/keepalive, deduped); `global-error.tsx` reports too. No external SDK — a Sentry/DataDog provider is still a pre-GA choice if you want dashboards (`LOG_CLIENT_ERRORS` env, default true). |
| 9 | **Full i18n of feature UI** | Every page/feature string extracted into next-intl (`messages/en.json` + `cs.json`, **14 namespaces, 390 keys, full EN/CS parity**). Server components use `getTranslations`, client use `useTranslations`; zod messages via `build*Schema(t)` factories; ICU interpolation/rich tags where needed. The locale toggle now translates the whole app. See **i18n notes** below for the two intentional carve-outs. |
| 10 | **Platform-admin pages** | Read-only cross-tenant views. Backend: `GET /v1/identity/platform/users` (paged, `IgnoreQueryFilters` + re-added soft-delete guard, new permission `platform.users.list`, auto-seeded + admin-granted) and `GET /v1/identity/platform/users/{id}/audit` (reuses the existing cross-tenant audit query, gated on `audit.read`). FE: `/platform/users` + `/platform/audit` pages, `platform-users-table`, nav re-added; audit view reuses the identity-admin audit-trail component. **No** cross-tenant role mutation (out of scope). |

## Resolved — decision recorded

| # | Finding | Decision (2026-06-21) |
|---|---|---|
| 5 | **Orphaned consent types** (`analytics`, `third_party_sharing`) | **KEEP** as forward-looking preferences. The toggles are wired to the real (now versioned, #2) consent ledger, so they capture provable consent *before* the analytics / third-party features exist — which is the GDPR-positive ordering (consent-first). No feature reads these yet; a future analytics/third-party integration must gate on a granted consent. Removing user-facing privacy controls is an outward-facing change, so it is intentionally NOT done here — say the word and the two toggles can be removed until the functionality lands. |

## i18n notes (#9) — intentional carve-outs

- **Legal long-form (terms/privacy body):** the page **chrome** (titles, "Last updated", section headings, buttons) is fully
  localized. The long-form legal paragraphs keep **English as authoritative**; the Czech rendering is a **machine-drafted
  translation shown behind a visible "draft — English is legally authoritative" banner**. Have the Czech legal text
  professionally reviewed before relying on it.
- **Vendored shadcn primitives:** 4 screen-reader `aria-label`s in upstream-vendored primitives
  (`components/ui/pagination.tsx` "Go to previous/next page", `components/ui/sidebar.tsx` "Toggle Sidebar") are left as the
  English upstream defaults to avoid drift on shadcn updates. Localize via props if a fully-translated a11y tree is required.
- **`/design` gallery** is dev-only (404 in prod) and intentionally not translated.

## Pre-GA infra choices still open (not blockers)

- **Observability provider (#8):** own `/api/log` ships now; pick Sentry/DataDog for dashboards + alerting before GA.
- **KEK/KMS envelope-wrapping of DEKs**, **shared rate-limit store**, and other items from the platform `docs/ROADMAP.md`
  remain as documented there.

*Originally generated from the 2026 frontend gap audit; updated 2026-06-20 after implementing 9/10 follow-ups.*
