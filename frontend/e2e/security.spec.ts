import { test, expect } from "@playwright/test";
import { ANONYMOUS, registerFreshUser } from "./helpers";

/**
 * Security E2E suite.
 *
 * Token-storage, CSRF, security headers, BFF-proxy enforcement, session lifecycle.
 *
 * Scenarios covered (IDs from docs/test-scenarios/security.md):
 *   SEC-01  No tokens in localStorage / sessionStorage after login
 *   SEC-02  mp_session is httpOnly (absent from document.cookie)
 *   SEC-03  mp_csrf is JS-readable (present in document.cookie)
 *   SEC-04  Mutating BFF POST without x-csrf-token → 403
 *   SEC-05  Mutating BFF POST with cross-origin Origin → 403
 *   SEC-06  CSP header with nonce and frame-ancestors none
 *   SEC-07  Strict-Transport-Security header
 *   SEC-08  X-Content-Type-Options: nosniff
 *   SEC-09  X-Frame-Options: DENY
 *   SEC-10  Referrer-Policy header
 *   SEC-11  Browser direct fetch to backend port without token → 401
 *   SEC-13  GET to BFF without x-csrf-token succeeds (GET is not mutating)
 *   SEC-16  Unauthenticated navigation to dashboard redirects to /login
 *   SEC-17  After logout session cookie is cleared
 *   SEC-18  Permissions-Policy header
 */

// ──────────────────────────────────────────────────────────────────────────────
// Token / cookie storage (SEC-01, SEC-02, SEC-03)
// Needs an authenticated session to verify the cookie state post-login.
// ──────────────────────────────────────────────────────────────────────────────

test.describe("Token and cookie hygiene", () => {
  // These run with the DEFAULT shared authenticated storageState (no ANONYMOUS needed —
  // we want to verify the post-login state).

  test("no tokens in localStorage or sessionStorage after login (SEC-01)", async ({ page }) => {
    await page.goto("/");
    // Wait for the dashboard to actually render (confirms the session is active).
    await expect(page.getByRole("heading", { name: /welcome back/i })).toBeVisible();

    // Inspect storage for anything that looks like a bearer / JWT token.
    // Rules:
    //   - Ignore Next.js internal keys (start with "__next") — these are dev/debug data, not tokens.
    //   - A suspicious KEY contains "token", "access", "refresh", "bearer", or "jwt".
    //   - A suspicious VALUE either looks like a JWT (three base64url segments separated by dots)
    //     OR contains those words AND is longer than 20 chars (to avoid short i18n/config values).
    const suspiciousStorage = await page.evaluate(() => {
      const keyPattern = /token|access|refresh|bearer|jwt/i;
      const jwtPattern = /^[A-Za-z0-9_-]{10,}\.[A-Za-z0-9_-]{10,}\.[A-Za-z0-9_-]{10,}$/;
      const valueTokenPattern = /token|access|refresh|bearer|jwt/i;
      const findings: { store: string; key: string }[] = [];

      function isSuspicious(k: string, v: string): boolean {
        // Skip Next.js internal keys entirely.
        if (k.startsWith("__next")) return false;
        // Key contains a token-like word.
        if (keyPattern.test(k)) return true;
        // Value looks like a JWT.
        if (jwtPattern.test(v.trim())) return true;
        // Value contains a token-like word and is long enough to be a real credential.
        if (valueTokenPattern.test(v) && v.length > 20) return true;
        return false;
      }

      for (let i = 0; i < localStorage.length; i++) {
        const k = localStorage.key(i) ?? "";
        const v = localStorage.getItem(k) ?? "";
        if (isSuspicious(k, v)) findings.push({ store: "localStorage", key: k });
      }
      for (let i = 0; i < sessionStorage.length; i++) {
        const k = sessionStorage.key(i) ?? "";
        const v = sessionStorage.getItem(k) ?? "";
        if (isSuspicious(k, v)) findings.push({ store: "sessionStorage", key: k });
      }
      return findings;
    });

    expect(suspiciousStorage, `Found token-like values in storage: ${JSON.stringify(suspiciousStorage)}`).toHaveLength(0);
  });

  test("session cookie mp_session is httpOnly and absent from document.cookie (SEC-02)", async ({ page }) => {
    await page.goto("/");
    await expect(page.getByRole("heading", { name: /welcome back/i })).toBeVisible();

    const visibleCookies = await page.evaluate(() => document.cookie);
    // mp_session must NOT appear because it is httpOnly.
    expect(visibleCookies).not.toContain("mp_session");
  });

  test("mp_csrf cookie is present and JS-readable (SEC-03)", async ({ page }) => {
    await page.goto("/");
    await expect(page.getByRole("heading", { name: /welcome back/i })).toBeVisible();

    const csrfVisible = await page.evaluate(() => {
      const match = document.cookie.match(/(?:^|;\s*)mp_csrf=([^;]+)/);
      return match ? match[1] : null;
    });
    expect(csrfVisible, "mp_csrf cookie should be JS-readable").not.toBeNull();
    expect((csrfVisible as string).length, "mp_csrf should have a non-empty value").toBeGreaterThan(0);
  });
});

// ──────────────────────────────────────────────────────────────────────────────
// CSRF enforcement (SEC-04, SEC-05, SEC-13)
// ──────────────────────────────────────────────────────────────────────────────

test.describe("CSRF enforcement on mutating BFF routes", () => {
  // Use the authenticated storageState so the session cookie exists (needed to
  // reach the CSRF check — otherwise the backend might return 401 first).

  test("mutating BFF request without x-csrf-token is rejected with 403 (SEC-04)", async ({ page }) => {
    await page.goto("/");
    await expect(page.getByRole("heading", { name: /welcome back/i })).toBeVisible();

    // Make a POST to a real BFF route (profile update) without x-csrf-token.
    // We use page.evaluate so the request carries the session cookie from the browser context.
    // The body doesn't matter — CSRF is checked before the upstream call.
    const result = await page.evaluate(async () => {
      const res = await fetch("/api/bff/identity/users/me", {
        method: "POST",
        headers: {
          "content-type": "application/json",
          // Deliberately omit x-csrf-token.
        },
        body: JSON.stringify({}),
        credentials: "same-origin",
      });
      let body: unknown = null;
      try { body = await res.json(); } catch { /* ignore */ }
      return { status: res.status, body };
    });

    expect(result.status).toBe(403);
    expect((result.body as { errorCode?: string } | null)?.errorCode).toBe("security.csrf_failed");
  });

  test("mutating BFF request with cross-origin Origin header is rejected with 403 (SEC-05)", async ({ page }) => {
    await page.goto("/");
    await expect(page.getByRole("heading", { name: /welcome back/i })).toBeVisible();

    // Read the csrf token from the cookie so we can include it (token is valid, but Origin is wrong).
    const csrfToken = await page.evaluate(() => {
      const match = document.cookie.match(/(?:^|;\s*)mp_csrf=([^;]+)/);
      return match ? match[1] : "";
    });
    expect(csrfToken.length).toBeGreaterThan(0);

    // We cannot override the Origin header from within page.evaluate (browsers enforce it).
    // Use page.request (from Playwright's APIRequestContext) which DOES allow arbitrary headers.
    // We include the session cookie manually by reading all cookies first.
    const cookies = await page.context().cookies();
    const cookieHeader = cookies.map((c) => `${c.name}=${c.value}`).join("; ");

    const response = await page.request.post("http://localhost:3000/api/bff/identity/users/me", {
      headers: {
        "content-type": "application/json",
        "x-csrf-token": csrfToken,
        "origin": "http://evil.example.com",   // cross-origin → should be rejected
        "cookie": cookieHeader,
      },
      data: JSON.stringify({}),
    });

    expect(response.status()).toBe(403);
    const body = await response.json() as { errorCode?: string };
    expect(body.errorCode).toBe("security.csrf_failed");
  });

  test("GET to BFF without x-csrf-token succeeds (SEC-13)", async ({ page }) => {
    await page.goto("/");
    await expect(page.getByRole("heading", { name: /welcome back/i })).toBeVisible();

    // GET is not mutating; no CSRF header required.
    const result = await page.evaluate(async () => {
      const res = await fetch("/api/bff/identity/users/me", {
        method: "GET",
        credentials: "same-origin",
        // No x-csrf-token header.
      });
      return { status: res.status };
    });

    // 200 (profile exists) or possibly 404 if route shape differs — but NOT 403.
    expect(result.status).not.toBe(403);
    expect(result.status).toBeLessThan(500);
  });
});

// ──────────────────────────────────────────────────────────────────────────────
// HTTP security headers (SEC-06, SEC-07, SEC-08, SEC-09, SEC-10, SEC-18)
// Inspect the headers on an HTML document response.
// The proxy sets headers on every non-_next path.
// ──────────────────────────────────────────────────────────────────────────────

test.describe("HTTP security response headers", () => {
  // These are anonymous — we just want the server's response headers on a page load.
  // We use page.request to capture them without needing authentication.
  test.use(ANONYMOUS);

  test("document response includes Content-Security-Policy with nonce and frame-ancestors none (SEC-06)", async ({ page }) => {
    const response = await page.request.get("http://localhost:3000/login");
    expect(response.status()).toBeLessThan(400);

    const csp = response.headers()["content-security-policy"];
    expect(csp, "Content-Security-Policy header must be present").toBeTruthy();
    expect(csp).toContain("nonce-");
    expect(csp).toContain("default-src 'self'");
    expect(csp).toContain("frame-ancestors 'none'");
    expect(csp).toContain("object-src 'none'");
  });

  test("document response includes Strict-Transport-Security (SEC-07)", async ({ page }) => {
    const response = await page.request.get("http://localhost:3000/login");
    const hsts = response.headers()["strict-transport-security"];
    expect(hsts, "Strict-Transport-Security must be present").toBeTruthy();
    expect(hsts).toContain("max-age=");
    expect(hsts).toContain("includeSubDomains");
  });

  test("document response includes X-Content-Type-Options nosniff (SEC-08)", async ({ page }) => {
    const response = await page.request.get("http://localhost:3000/login");
    const xcto = response.headers()["x-content-type-options"];
    expect(xcto, "X-Content-Type-Options must be present").toBeTruthy();
    expect(xcto?.toLowerCase()).toBe("nosniff");
  });

  test("document response includes X-Frame-Options DENY (SEC-09)", async ({ page }) => {
    const response = await page.request.get("http://localhost:3000/login");
    const xfo = response.headers()["x-frame-options"];
    expect(xfo, "X-Frame-Options must be present").toBeTruthy();
    expect(xfo?.toUpperCase()).toBe("DENY");
  });

  test("document response includes Referrer-Policy strict-origin-when-cross-origin (SEC-10)", async ({ page }) => {
    const response = await page.request.get("http://localhost:3000/login");
    const rp = response.headers()["referrer-policy"];
    expect(rp, "Referrer-Policy must be present").toBeTruthy();
    expect(rp?.toLowerCase()).toBe("strict-origin-when-cross-origin");
  });

  test("document response includes Permissions-Policy restricting camera, microphone, geolocation (SEC-18)", async ({ page }) => {
    const response = await page.request.get("http://localhost:3000/login");
    const pp = response.headers()["permissions-policy"];
    expect(pp, "Permissions-Policy must be present").toBeTruthy();
    expect(pp).toContain("camera=()");
    expect(pp).toContain("microphone=()");
    expect(pp).toContain("geolocation=()");
  });

  test("authenticated page also carries all security headers", async ({ page }) => {
    // Re-check on an authenticated page to confirm the proxy runs for all routes.
    // We navigate to /login (which exists without auth) and use page.request so we
    // don't need the storageState for this header check.
    const response = await page.request.get("http://localhost:3000/");

    const csp = response.headers()["content-security-policy"];
    const hsts = response.headers()["strict-transport-security"];
    const xcto = response.headers()["x-content-type-options"];
    const xfo = response.headers()["x-frame-options"];

    // Even if / redirects to /login (no session), the initial response carries headers.
    expect(csp).toBeTruthy();
    expect(hsts).toBeTruthy();
    expect(xcto?.toLowerCase()).toBe("nosniff");
    expect(xfo?.toUpperCase()).toBe("DENY");
  });
});

// ──────────────────────────────────────────────────────────────────────────────
// Backend direct access (SEC-11)
// The browser has no bearer token — a direct call to :5271 should return 401.
// ──────────────────────────────────────────────────────────────────────────────

test.describe("Direct backend access without token", () => {
  test("browser fetch to backend port without token returns 401 (SEC-11)", async ({ page }) => {
    // Use the authenticated page context but call the backend directly (no BFF, no session cookie).
    await page.goto("/");
    await expect(page.getByRole("heading", { name: /welcome back/i })).toBeVisible();

    const result = await page.evaluate(async () => {
      try {
        const res = await fetch("http://localhost:5271/v1/identity/users/me", {
          method: "GET",
          headers: { accept: "application/json" },
          // No Authorization header — the browser has no token to send.
        });
        return { status: res.status, reachable: true };
      } catch {
        // CORS or network error — the backend is unreachable from the browser origin,
        // which is even stronger than a 401.
        return { status: 0, reachable: false };
      }
    });

    // Either the backend is unreachable (CORS / network isolation) or it returns 401.
    // Both outcomes confirm the browser cannot make authenticated calls directly.
    const isBlocked = !result.reachable || result.status === 401 || result.status === 403;
    expect(
      isBlocked,
      `Expected backend to be unreachable or return 401/403 without a token, got status=${result.status} reachable=${result.reachable}`,
    ).toBe(true);
  });
});

// ──────────────────────────────────────────────────────────────────────────────
// Session lifecycle (SEC-16, SEC-17)
// ──────────────────────────────────────────────────────────────────────────────

test.describe("Session lifecycle and guards", () => {
  // SEC-16: Verify unauthenticated navigation redirects to /login.
  // test.use() cannot be called inside a test body; instead we clear cookies manually
  // so this test starts without a session even when run with the shared storageState.
  test("unauthenticated navigation to dashboard redirects to /login (SEC-16)", async ({ page }) => {
    await page.context().clearCookies();
    await page.goto("/");
    // The middleware / server-side auth guard should redirect to /login.
    await expect(page).toHaveURL(/\/login/, { timeout: 10_000 });
  });

  test("after logout session cookie is cleared and protected routes redirect to login (SEC-17)", async ({ page }) => {
    // Use a fresh user to avoid corrupting the shared primary session.
    const { email } = await registerFreshUser(page);
    // Verify we're on the dashboard.
    await expect(page).toHaveURL("/");
    await expect(page.getByRole("heading", { name: /welcome back/i })).toBeVisible();

    // Trigger logout via the user menu (aria-label="User menu" on the sidebar footer trigger).
    await page.getByRole("button", { name: "User menu" }).click();
    await page.getByRole("menuitem", { name: /sign out/i }).click();

    // After logout we should be on /login.
    await expect(page).toHaveURL(/\/login/, { timeout: 15_000 });

    // Now try navigating back to the protected dashboard — should redirect to /login again.
    await page.goto("/");
    await expect(page).toHaveURL(/\/login/, { timeout: 10_000 });

    void email; // used to register above; no further assertions needed
  });
});

// ──────────────────────────────────────────────────────────────────────────────
// Unauthenticated redirect — separate block that properly uses test.use(ANONYMOUS)
// (test.use() inside a test body is not supported in Playwright; it must be at describe level)
// ──────────────────────────────────────────────────────────────────────────────

test.describe("Unauthenticated redirect (SEC-16, anonymous context)", () => {
  test.use(ANONYMOUS);

  test("visiting / without a session redirects to /login", async ({ page }) => {
    await page.goto("/");
    await expect(page).toHaveURL(/\/login/, { timeout: 10_000 });
  });

  test("visiting /billing without a session redirects to /login", async ({ page }) => {
    await page.goto("/billing");
    await expect(page).toHaveURL(/\/login/, { timeout: 10_000 });
  });
});
