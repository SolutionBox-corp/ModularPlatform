# Security — Test Scenario Catalog

Cross-cutting browser security checks for the ModularPlatform frontend. Covers token storage,
BFF-only API traffic, CSRF defence, HTTP security headers, and the absence of XSS sinks.

The BFF model: the browser **never** holds bearer tokens. The encrypted `mp_session` cookie
(httpOnly, iron-session) contains both the access and refresh tokens. The only JS-readable
cookie is `mp_csrf` (the double-submit CSRF token). All `/v1` traffic is proxied through
`/api/bff/[...path]`; mutating requests (POST/PUT/PATCH/DELETE) require a matching
`x-csrf-token` header and a same-origin `Origin` header, or the route returns 403.

---

## SEC-01 — No tokens in localStorage or sessionStorage after login

- **Given** a user visits `/login` and authenticates successfully
- **When** the dashboard loads
- **Then** `localStorage` and `sessionStorage` contain no keys whose names or values
  look like bearer tokens (no key contains "token", "access", "refresh", "bearer", "jwt")
- Priority: P0 · Type: security · Automated: yes (e2e: `no tokens in localStorage or sessionStorage after login`)

---

## SEC-02 — Session cookie is httpOnly (not JS-readable)

- **Given** a user is authenticated
- **When** the page inspects `document.cookie`
- **Then** `mp_session` does NOT appear in `document.cookie` (it is httpOnly)
- Priority: P0 · Type: security · Automated: yes (e2e: `session cookie mp_session is httpOnly and absent from document.cookie`)

---

## SEC-03 — CSRF cookie mp_csrf is JS-readable (double-submit design)

- **Given** any page load (authenticated or not)
- **When** the page inspects `document.cookie`
- **Then** `mp_csrf` IS present in `document.cookie` with a non-empty value
- Priority: P1 · Type: security · Automated: yes (e2e: `mp_csrf cookie is present and JS-readable`)

---

## SEC-04 — Mutating BFF request without x-csrf-token returns 403

- **Given** the browser is authenticated (session cookie present)
- **When** a POST is made to `/api/bff/identity/users/me` (or any mutating path) WITHOUT
  the `x-csrf-token` header (but with a valid `Origin` header)
- **Then** the response status is 403 and the body contains `errorCode: "security.csrf_failed"`
- Priority: P0 · Type: security · Automated: yes (e2e: `mutating BFF request without x-csrf-token is rejected with 403`)

---

## SEC-05 — Mutating BFF request without same-origin Origin returns 403

- **Given** the browser is authenticated
- **When** a POST is made to `/api/bff/identity/users/me` with a valid `x-csrf-token` but
  with a cross-origin `Origin` header (e.g. `http://evil.example.com`)
- **Then** the response status is 403 with `errorCode: "security.csrf_failed"`
- Priority: P0 · Type: security · Automated: yes (e2e: `mutating BFF request with cross-origin Origin is rejected with 403`)

---

## SEC-06 — Response headers include Content-Security-Policy with nonce

- **Given** a GET request to any HTML page (e.g. `/`)
- **When** the response headers are inspected
- **Then** `Content-Security-Policy` is present and contains `nonce-` (per-request nonce),
  `default-src 'self'`, and `frame-ancestors 'none'`
- Priority: P0 · Type: security · Automated: yes (e2e: `document response includes Content-Security-Policy with nonce and frame-ancestors none`)

---

## SEC-07 — Response headers include HSTS

- **Given** a GET request to any HTML page
- **When** response headers are inspected
- **Then** `Strict-Transport-Security` is present and contains `max-age=` and `includeSubDomains`
- Priority: P1 · Type: security · Automated: yes (e2e: `document response includes Strict-Transport-Security`)
- Note: HSTS is only meaningful over TLS. The header is set unconditionally in `next.config.ts`
  and will be present in dev (http) too, which is what the E2E suite tests. A production TLS
  check is a manual/infrastructure concern.

---

## SEC-08 — Response headers include X-Content-Type-Options: nosniff

- **Given** a GET request to any HTML page
- **When** response headers are inspected
- **Then** `X-Content-Type-Options` equals `nosniff`
- Priority: P1 · Type: security · Automated: yes (e2e: `document response includes X-Content-Type-Options nosniff`)

---

## SEC-09 — Response headers include X-Frame-Options: DENY

- **Given** a GET request to any HTML page
- **When** response headers are inspected
- **Then** `X-Frame-Options` equals `DENY`
- Priority: P1 · Type: security · Automated: yes (e2e: `document response includes X-Frame-Options DENY`)

---

## SEC-10 — Response headers include Referrer-Policy

- **Given** a GET request to any HTML page
- **When** response headers are inspected
- **Then** `Referrer-Policy` equals `strict-origin-when-cross-origin`
- Priority: P1 · Type: security · Automated: yes (e2e: `document response includes Referrer-Policy strict-origin-when-cross-origin`)

---

## SEC-11 — Browser cannot directly reach the .NET backend

- **Given** a user is authenticated in the browser
- **When** the browser tries to fetch `http://localhost:5271/v1/identity/users/me` directly
  (bypassing the BFF, no bearer token)
- **Then** the request returns 401 (no token) and the browser has no way to read the
  session token to forge an authenticated direct request
- Priority: P1 · Type: security · Automated: yes (e2e: `browser fetch to backend port without token returns 401`)
- Note: The test exercises the absence of a bearer token from the browser context, not a
  network-level firewall. In production the backend port should not be publicly reachable;
  that is an infrastructure concern marked manual.

---

## SEC-12 — No dangerouslySetInnerHTML in application source

- **Given** the application source files
- **When** the codebase is scanned for `dangerouslySetInnerHTML` and `__html`
- **Then** none are found in `app/`, `components/`, `features/`, or `lib/` (only in
  `node_modules` and generated code is acceptable)
- Priority: P0 · Type: security · Automated: manual (static grep at build time / in CI)
- Rationale: Playwright cannot inspect the source; this is a code-review/CI lint check.
  A grep in CI (`grep -r dangerouslySetInnerHTML app components features lib`) should
  return exit 1 if any match is found.

---

## SEC-13 — GET requests to BFF are NOT subject to CSRF check (idempotency rule)

- **Given** the browser is authenticated
- **When** a GET is made to `/api/bff/identity/users/me` WITHOUT `x-csrf-token`
- **Then** the response is 200 (GET is not mutating; CSRF check is skipped)
- Priority: P1 · Type: security · Automated: yes (e2e: `GET to BFF without x-csrf-token succeeds`)

---

## SEC-14 — CSP blocks inline scripts (no nonce = blocked)

- **Given** the CSP is active with `nonce-{n}` and `strict-dynamic`
- **When** an inline `<script>` without the correct nonce tries to run in the page
- **Then** the browser blocks it (CSP violation) — script does not execute
- Priority: P0 · Type: security · Automated: manual
- Rationale: Testing CSP enforcement requires injecting an inline script and observing
  a CSP violation report or blocked execution. This is not feasible in E2E without a
  CSP violation endpoint; verify by reviewing the `buildCsp` function and nonce usage
  in the layout. Confirmed: `app/layout.tsx` passes the nonce from headers to `Providers`.

---

## SEC-15 — x-tenant header spoofing is stripped by the proxy

- **Given** a browser request that includes a custom `x-tenant` header
- **When** the proxy processes the request
- **Then** the spoofed header is deleted and a server-authoritative `x-tenant` is injected
  based on the `Host` header
- Priority: P1 · Type: security · Automated: manual
- Rationale: The stripping happens inside the Edge proxy (`proxy.ts:requestHeaders.delete(TENANT_HEADER)`)
  before the request reaches the app. Verifying this at the HTTP level would require
  inspecting the upstream backend request, which is not accessible from the browser in E2E.
  Covered by unit/integration tests of `proxy.ts`.

---

## SEC-16 — Unauthenticated user is redirected to /login (session guard)

- **Given** a user with no session cookie navigates to `/` (the protected dashboard)
- **When** the page loads
- **Then** the browser is redirected to `/login` (with an optional `next` param)
- Priority: P0 · Type: security · Automated: yes (e2e: `unauthenticated navigation to dashboard redirects to login`)

---

## SEC-17 — After logout, session cookie is cleared (tokens inaccessible)

- **Given** an authenticated user is on the dashboard
- **When** they log out via the user menu
- **Then** they are redirected to `/login`, and `mp_session` is no longer set
  (the httpOnly cookie is cleared by `session.destroy()`)
- Priority: P0 · Type: security · Automated: yes (e2e: `after logout session cookie is cleared`)
- Note: Because `mp_session` is httpOnly, the test verifies it by confirming that a
  subsequent navigation to `/` redirects to `/login` (i.e. the session is gone).

---

## SEC-18 — Permissions-Policy header restricts sensitive APIs

- **Given** a GET request to any HTML page
- **When** response headers are inspected
- **Then** `Permissions-Policy` is present and restricts `camera`, `microphone`, `geolocation`
- Priority: P2 · Type: security · Automated: yes (e2e: `document response includes Permissions-Policy`)
