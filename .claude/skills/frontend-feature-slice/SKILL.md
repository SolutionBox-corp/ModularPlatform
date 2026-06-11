---
name: frontend-feature-slice
description: Add a feature slice to the ModularPlatform Next.js frontend (a domain page/section over the /v1 API — billing, files, notifications, gdpr, operations, account, admin). Use when adding any FE data-bound feature. Enforces the single-data-source + colocation patterns so data is fetched once and reused, with no god objects.
---

# Frontend feature slice

**Read the `modularplatform-frontend` skill first** (the 4 seams must already exist). A feature = `features/<domain>/`
with four small files; the route segment under `app/` only prefetches + composes. **Reuse the seams — never re-do
auth/fetch/error/realtime per feature.**

## The 4 feature files (small, single-responsibility)
1. **`schema.ts`** — zod schemas for the form input AND the response shape (`z.infer` → types). One place for shapes.
2. **`api.ts`** — the ONLY place this domain talks to the API:
   - **Queries** = `queryOptions(...)` factories over `apiFetch` (NEVER inline `queryKey`). Co-locate a key namespace:
     ```ts
     export const billingKeys = { all: ['billing'] as const, balance: () => [...billingKeys.all, 'balance'] as const };
     export const billingQueries = {
       balance: () => queryOptions({ queryKey: billingKeys.balance(), queryFn: () => apiFetch<Balance>('/billing/credits/balance'), staleTime: 30_000 }),
     };
     ```
   - **Mutations** = plain async fns over `apiFetch` (e.g. `checkout(packageId)`), consumed by `useMutation` in hooks.
3. **`hooks.ts`** — thin wrappers: `useBalance() => useQuery(billingQueries.balance())`;
   `useCheckout() => useMutation({ mutationFn: checkout, onSuccess: () => qc.invalidateQueries({ queryKey: billingKeys.all }) })`.
4. **`components/`** — leaf `'use client'` islands composing `components/ui` primitives; read via the hooks, render
   states (loading=Skeleton, error=`<ProblemDetails>`, empty=`<EmptyState>`, data). Money/metrics via `<MoneyAmount>`.

## Wire it
- **Route segment** (`app/(app)/<domain>/page.tsx`) = a Server Component: `prefetchQuery(billingQueries.balance())`
  (no await — stream), wrap in `<HydrationBoundary state={dehydrate(getQueryClient())}>`, render the client component.
  Add `loading.tsx` (Skeleton) + `error.tsx` (route boundary).
- **Realtime:** if this domain changes live, add `eventType → queryKey` to the central realtime map (in the provider) —
  do NOT subscribe to SSE in the component. The component just `useQuery`s and goes live on invalidation.
- **Long-running (202 + Location):** reuse the shared `operations` polling hook (`useOperation(id)`) — don't re-poll.
- **Forms:** `react-hook-form` + zodResolver; on a 422/validation `ApiError`, map `errors[].field/errorCode` onto fields.
- **i18n:** any user-facing string via next-intl; any backend failure rendered via the single `errorCode→message` mapper.

## Add it to /design FIRST
Render the new feature's components in the `/design` gallery (all states, sample data) and approve the design before
wiring the real page.

## NEVER
`useEffect` fetch · inline `queryKey` · a second SSE connection · per-component error text / swallowed errors · reach
into another feature's internals (use its public `api.ts`/`hooks.ts`) · a god hook/store · `dangerouslySetInnerHTML` ·
token in JS storage · hardcoded colors/spacing (use tokens) · axios/Redux.

## Verify
`pnpm typecheck` + `pnpm lint` clean · the data is fetched once (server prefetch) and reused (no refetch flash) · the
component appears in `/design` in all states · live updates arrive via central invalidation, not a local SSE hook.
