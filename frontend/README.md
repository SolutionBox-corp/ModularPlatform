# ModularPlatform Frontend

A Next.js (App Router) frontend for the ModularPlatform SaaS base. It acts as a BFF (Backend-for-Frontend)
over the .NET `/v1` API — all API calls from the browser go through Next.js server routes and server
actions; credentials never leave the server.

---

## Prerequisites

- Node.js 22+ and pnpm 9+
- The .NET backend running (see `src/hosts/ModularPlatform.Api`)
- A local Postgres instance (the backend handles this; the frontend has no direct DB access)

---

## Environment Variables

Copy `.env.example` to `.env.local` and fill in:

| Variable | Description |
|---|---|
| `BACKEND_URL` | Base URL of the .NET API, e.g. `http://localhost:5271` |
| `SESSION_PASSWORD` | 32+ character random secret for iron-session encryption |
| `ROOT_DOMAIN` | Root domain for subdomain-per-tenant routing. Dev: `lvh.me:3000` |
| `ALLOWED_ORIGINS` | Optional extra trusted origins (comma-separated) |
| `NEXT_PUBLIC_APP_NAME` | App name shown in the UI |

---

## Running Locally

Start the .NET backend first, then:

```bash
pnpm install
pnpm dev
```

Open `http://app.lvh.me:3000` (not `localhost:3000`). The `lvh.me` subdomain resolves to `127.0.0.1`
and is required for subdomain-per-tenant routing and HSTS-safe cookies. `ROOT_DOMAIN=lvh.me:3000`
must be set in `.env.local`.

> **HSTS note:** Do not use `http://localhost:3000` in production-like testing — the session cookie
> is `Secure` by default. In development Next.js is HTTP; the cookie works because `127.0.0.1` /
> `lvh.me` subdomains are excluded from HSTS preload lists.

---

## Architecture Seams

| Seam | Where |
|---|---|
| **BFF / API proxy** | `lib/server/backend.ts` — all server-side fetch calls to `/v1` |
| **Auth (iron-session)** | `lib/auth/session.ts` — JWT + refresh token stored server-side; never in the browser |
| **TanStack Query** | `lib/api/query-client.ts` + per-feature `hooks.ts` files — client-side data fetching and cache |
| **Server Actions** | `features/auth/actions.ts` and per-feature `actions.ts` — mutations that touch the backend |
| **Error mapper** | `lib/errors/error-map.ts` — translates RFC 9457 error codes to user-facing messages; all errors flow through `sonner` toasts |
| **SSE / Realtime** | `lib/realtime/` - one `EventSourcePlus` stream to `GET /api/bff/realtime/stream`; feature modules only add event-map invalidation rows |
| **Origins / multi-tenancy** | `lib/origins.ts` — derives allowed origins from `ROOT_DOMAIN`; every `*.lvh.me:3000` subdomain is a separate tenant in dev |
| **Cookie consent** | `components/app/cookie-consent.tsx` — vanilla-cookieconsent v3; necessary cookies always on, analytics/marketing opt-in |

---

## Project Structure

```
app/                    Next.js App Router pages
  (auth)/               Public auth pages (login, register) — no session required
  (tenant)/             Authenticated app shell — session enforced by layout
  terms/, privacy/      Public legal pages — no auth required
  api/bff/              BFF API routes (realtime SSE proxy, etc.)
  providers.tsx          Global client providers (QueryClient, theme, toaster, cookie consent)
components/
  ui/                   Base UI primitives (shadcn/Base UI)
  app/                  App-level composed components
features/               Feature slices (auth, billing, files, privacy, …)
  {feature}/
    api.ts              fetch wrappers (client-side)
    hooks.ts            TanStack Query hooks
    actions.ts          Server Actions
    components/         React components for this feature
lib/
  auth/                 iron-session helpers
  api/                  query client, types
  errors/               error-map + RFC 9457 types
  server/               server-only helpers (backend fetch, config)
docs/test-scenarios/    Given/When/Then test scenario catalog
e2e/                    Playwright E2E tests
```

---

## Running E2E Tests

The backend must be running and seeded. Then:

```bash
pnpm e2e          # run all tests headless
pnpm e2e:ui       # open Playwright UI
pnpm e2e:report   # show the last HTML report
```

See `docs/test-scenarios/` for the full Given/When/Then catalog that the E2E suite covers.
