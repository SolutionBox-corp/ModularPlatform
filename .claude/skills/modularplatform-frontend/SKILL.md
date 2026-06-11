---
name: modularplatform-frontend
description: Build/extend the ModularPlatform Next.js frontend. Use when scaffolding the web app, the /design component gallery, the app shell/menu, auth, realtime, error handling, or any UI over the .NET /v1 API. Enforces the frozen 2026 patterns — single API client (BFF), single data source (TanStack Query), one SSE realtime provider, centralized errors, security-by-design, GDPR, owned shadcn components.
---

# ModularPlatform frontend

**Read `~/Desktop/ModularPlatform-Frontend-Handoff.md` first** (full handoff: page map, component inventory, API
contract). This skill is the enforced architecture. **Next.js 15 App Router + React 19 + TS.** Backend = .NET 10
`/v1` REST: `{data}` success envelope, **RFC 9457** `problem+json` errors with stable `errorCode` + localized `detail`,
**JWT bearer + refresh ROTATION** (one-time-use, reuse-detection), realtime is **SSE** (`/v1/realtime/stream`,
Last-Event-ID) — **NOT SignalR/WebSocket**, 429 + `Retry-After`.

## Build order (don't skip)
1. Scaffold + stack (below) + design tokens.
2. **`/design` gallery** (skill below mentions it) — tokens → primitives → composed → app shell. Approve design here.
3. **The 4 cross-cutting seams** (§1-4) — BEFORE any feature.
4. Feature pages via the **frontend-feature-slice** skill, data through the seams only.

## §0 Multi-tenancy (subdomain-per-tenant) — ONE app, host-based routing
Full design: `docs/multitenancy-and-infra.md`. Tenant = a CUSTOMER at `{tenant}.nasedomena.cz`; `admin.` = the
platform-admin app (provisions tenants, toggles each tenant's modules); apex = landing. **ONE Next.js app** (not three).
- **`proxy.ts`** (Next 16 rename of `middleware.ts`): resolve `host` → rewrite to a route group `(marketing)` / `(admin)`
  / `(tenant)`; inject a **server-trusted `x-tenant`** header (strip any inbound client `x-tenant` first). Tenant identity
  is SOLELY the subdomain, never path/body (IDOR parity with backend Law 10).
- **Host-only session cookie** (httpOnly Secure SameSite=Lax, **NO `Domain` attribute** → bound to the exact subdomain;
  NEVER `Domain=.nasedomena.cz` = cross-tenant bleed). Runtime per-subdomain domain ⇒ not NextAuth.
- **Entitlement-driven nav (single source):** fetch `GET /v1/tenant/me/entitlements` once → TanStack key
  `['entitlements', tenant]` → drives ALL nav + route guards. The FE never hardcodes the module list (modules are
  per-tenant DATA, toggled by the platform-admin); a disabled module's route 404s/redirects (the backend guard enforces).
- Reserved slugs (`admin`/`www`/`api`) are not tenants. Dev hosts: `*.lvh.me` / `*.localhost`, `ROOT_DOMAIN=lvh.me:3000`.

## Stack (frozen)
Next 15 (RSC default) · @tanstack/react-query v5 (≥5.40) · one typed `apiFetch` (no axios) · `iron-session` BFF +
httpOnly refresh cookie · zod + react-hook-form · **shadcn/ui (owned, copy-in) + Radix + Tailwind v4** · realtime via
**`event-source-plus`** (one provider) · next-intl (en/cs) · sonner · vanilla-cookieconsent v3 · zustand v5 only for a
couple of UI flags. **NEVER:** axios · Redux · NextAuth/Auth.js · MUI/Mantine/Chakra/Ant · `@microsoft/fetch-event-source`
(dead) · native `EventSource` with token in URL · token in localStorage.

## §1 ONE API client + BFF (security + one place for auth/errors)
- The browser **never** calls .NET directly. Catch-all `app/api/bff/[...path]/route.ts` reads the access token from the
  encrypted `iron-session`, injects `Authorization: Bearer`, forwards to `/v1`. **Refresh token lives ONLY in an
  httpOnly Secure SameSite=Lax cookie** (a route handler rotates it). Access token in memory only — never localStorage.
- `apiFetch<T>(path, opts)` is the ONLY thing that talks to `/v1` (via BFF): attach bearer → `Accept-Language` → unwrap
  `{data}` → on 401 **single-flight refresh** (one shared `refreshPromise` under a module lock: N concurrent 401s ⇒
  exactly ONE `/refresh`, all retry once; second 401 ⇒ hard logout — mandatory because refresh rotates one-time-use) →
  parse problem+json into a typed `ApiError {status, errorCode, detail, retryAfter}`. Wire refresh into the client's
  retry seam, NOT QueryCache.onError (so mutations + one-offs are covered).
- CSRF: SameSite=Lax + Origin/Referer allowlist on mutating BFF routes + a signed double-submit token. No heavy lib.

## §2 ONE data source — TanStack Query (fetch once, use everywhere)
- `getQueryClient()`: **per-request** `new QueryClient()` on the server (a server singleton leaks data across users),
  **module-singleton** in the browser. Dehydrate pending queries (`shouldDehydrateQuery` incl. `status==='pending'`).
- **Inline `queryKey` arrays are BANNED.** Every read is a typed **`queryOptions(...)` factory** colocated in the
  feature's `api.ts` (`billingQueries.balance()` → `{queryKey, queryFn, staleTime}`). Server prefetch AND client
  `useQuery` import the SAME factory ⇒ keys can't drift. Invalidation uses the same keys.
- **RSC default; `useEffect`-fetching is BANNED.** Initial read: a Server Component calls
  `queryClient.prefetchQuery(...)` (no await — stream) and wraps the subtree in `<HydrationBoundary state={dehydrate(qc)}>`;
  client islands read via `useQuery` with the same key ⇒ zero refetch, zero flash. `'use client'` only on leaf
  interactive islands (forms, buttons, the realtime indicator).
- **Mutations** (login/checkout/upload/mark-read/consent) = client `useMutation` over `apiFetch`; on success
  `invalidateQueries({queryKey})`. No component fetches ad hoc. Server Actions only for cookie-writing auth steps.

## §3 ONE realtime provider (SSE) — never per-component
- One `<RealtimeProvider>` at the top: one `event-source-plus` stream to `/v1/realtime/stream`, header-auth (bearer),
  `retryStrategy:'always'` + backoff (cap ~30s) + auto Last-Event-ID resume **from the library** (never hand-rolled).
- The provider holds **NO domain state.** Each `{id, eventType, json}` → **one `eventType → queryKey` map** →
  `queryClient.invalidateQueries(...)`. Components just `useQuery` and stay live. `setQueryData` only for hot-append.
- Invalidate is idempotent ⇒ replayed/duplicate events are harmless (no client dedup tables). 401 on (re)connect →
  refresh → reconnect. `visibilitychange`: hidden → `abort()`, visible → `reconnect()`. Cleanup: `useEffect` returns
  `abort()`; logout calls `abort()`. **Banned:** per-component `useSSE`, a god `RealtimeManager` holding domain state,
  hand-rolled reconnect/backoff, a component that "knows eventTypes" to refetch.

## §4 Centralized errors
Three layers, ONE mapping: (1) `apiFetch` → typed `ApiError`; (2) one pure `errorCodeToMessage(errorCode, locale)` →
i18n key (next-intl), fallback to the API's localized `detail`, then a generic message — **internals NEVER shown**;
(3) `error.tsx` (per route) + `app/global-error.tsx` (root) + `QueryCache`/`MutationCache` `onError` → **sonner** toast
+ 429 `Retry-After` feedback. **Nothing is swallowed.** No per-component error text, no re-translating backend messages.

## §5 Security-by-design + GDPR (non-negotiable)
- **CSP** nonce + `strict-dynamic` generated per-request in `proxy.ts` (`default-src 'self'; script-src 'self'
  'nonce-{n}' 'strict-dynamic'; object-src 'none'; frame-ancestors 'none'; …`); sibling headers (HSTS, nosniff,
  Referrer-Policy, Permissions-Policy) in `next.config`. `require-trusted-types-for 'script'`.
- **XSS:** rely on React escaping; lint-ban `dangerouslySetInnerHTML` (`react/no-danger`). Raw HTML only via ONE
  `<SafeHtml>` wrapper using `isomorphic-dompurify` — never inline `DOMPurify.sanitize`.
- **GDPR:** vanilla-cookieconsent opt-in, granular categories (necessary always-on = auth/CSRF cookies), NO
  non-essential script/cookie before consent, consent decision + version + timestamp recorded (auditable). Terms/Privacy
  acceptance is **server-recorded** (the `gdpr/consents` domain), not client-only. Surface export (`/gdpr/me/export` →
  202 + poll the operation) + erase (`/gdpr/me/erase`, destructive confirm) in `/account/privacy`.

## §6 Structure (feature colocation, no god objects)
`app/` = thin route segments (prefetch + compose only). `features/<domain>/` each = `api.ts` (queryOptions + mutations
over apiFetch) + `hooks.ts` + `components/` + `schema.ts` (zod). `lib/` = the shared seams once (api-client,
query-client, realtime, error mapper, i18n). `components/ui/` = owned shadcn primitives; `components/app/` = AppShell,
ProblemDetails, MoneyAmount, DataTable… **A feature imports another ONLY via its public `api.ts`/`hooks.ts`** (mirrors
backend `*.Contracts`). No `utils.ts` dump, no 1000-line context/store. Server state = Query; UI state = local useState;
shared UI flags = zustand/context only if truly shared. **No Redux.**

## §7 Design tokens + a11y (see the /design gallery)
`tailwind.cssVariables:true`; tokens once in `globals.css` via Tailwind v4 `@theme inline` + CSS custom properties in
**OKLCH**; semantic pairs only (`--background/--foreground`, `--primary`, `--muted`, `--accent`, `--destructive`,
`--border`, `--ring`, `--radius`). **Dark = redefine the SAME variables under `.dark`.** `font-variant-numeric:
tabular-nums` globally on money/metrics/tables. Inter (UI) + Geist Mono (ids/payloads). **WCAG 2.2 AA** via Radix
(never hand-roll a11y); `--ring` on `:focus-visible`; target ≥24px; motion 150-250ms, all in `prefers-reduced-motion`.

## NO SLOP (the 2026 bans)
glassmorphism / heavy gradients / neon / parallax / blurred cards · hardcoded hex/spacing (use tokens) · god
Auth/Api/Error/Realtime "service" classes · swallowed errors / ignored 429 · localStorage tokens · per-page error
handling · multiple SSE connections · re-implementing backend messages client-side · a full UI library · Storybook as
the primary gallery now.

## Verify
`pnpm build` + `pnpm lint` (incl. `react/no-danger`) clean · `pnpm typecheck` clean · the /design gallery renders every
primitive in all states · no token in JS storage · one SSE connection in the network tab.
