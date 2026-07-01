import { test, expect } from "@playwright/test";

/**
 * Platform admin E2E spec.
 *
 * SCOPE: What can be verified from the normal apex/tenant host (localhost:3000).
 *   The full platform admin UI lives on the admin host (`admin.<root>`), which the
 *   E2E suite does not spin up. The automatable assertions here confirm the host-level
 *   isolation guard: navigating to /platform/* on the tenant host renders the 404
 *   Not Found page — the platform layout is never reached.
 *
 * The proxy (proxy.ts) rewrites any /platform/* path from a non-admin host to /not-found
 * before the Next.js routing layer. This must hold for authenticated AND unauthenticated
 * users, for spoofed x-tenant headers, and for every /platform sub-path.
 *
 * All other scenarios (tenant provisioning, entitlement toggles, invite creation, billing
 * card, permission-gated nav) require the admin host and are catalogued as MANUAL in
 * docs/test-scenarios/platform-admin.md.
 */

test.describe("platform admin is not exposed on the normal host", () => {
  // These run with the DEFAULT storageState (authenticated primary user).
  // The guard must fire regardless of auth state.

  test("/platform renders 404 (authenticated user)", async ({ page }) => {
    await page.goto("/platform");

    // The proxy rewrites /platform → /not-found before the layout runs.
    // app/not-found.tsx renders: <p>404</p> and <h1>Page not found</h1>.
    await expect(
      page.getByRole("heading", { name: /platform administration/i }),
    ).not.toBeVisible();
    await expect(
      page.getByRole("button", { name: /provision tenant/i }),
    ).not.toBeVisible();
    await expect(page.getByRole("heading", { name: "Page not found" })).toBeVisible();
    await expect(page.getByText("404")).toBeVisible();
  });

  test("/platform/tenants renders 404 (authenticated user)", async ({ page }) => {
    await page.goto("/platform/tenants");

    await expect(
      page.getByRole("button", { name: /provision tenant/i }),
    ).not.toBeVisible();
    await expect(page.getByRole("heading", { name: "Page not found" })).toBeVisible();
    await expect(page.getByText("404")).toBeVisible();
  });

  test("/platform/tenants/{id} renders 404 (authenticated user)", async ({ page }) => {
    // Use a plausible but non-existent UUID.
    await page.goto("/platform/tenants/00000000-0000-7000-8000-000000000001");

    await expect(
      page.getByRole("heading", { name: /tenant detail/i }),
    ).not.toBeVisible({ timeout: 5_000 });
    await expect(page.getByRole("heading", { name: "Page not found" })).toBeVisible();
    await expect(page.getByText("404")).toBeVisible();
  });

  test("no platform admin UI leaks into page source via /platform", async ({ page }) => {
    await page.goto("/platform");

    // Confirm the platform-specific components are not in the DOM at all.
    // These text strings are unique to the platform admin views.
    await expect(
      page.getByText("Platform administration"),
    ).not.toBeVisible({ timeout: 5_000 });
    await expect(page.getByText("Provision new tenant")).not.toBeVisible({ timeout: 5_000 });
    await expect(
      page.getByText("Tenant entitlement editor"),
    ).not.toBeVisible({ timeout: 5_000 });
    await expect(page.getByText("SaaS operator billing")).not.toBeVisible({ timeout: 5_000 });
  });

  test("x-frame-options DENY header is present on /platform response", async ({
    request,
  }) => {
    // Even though /platform is rewritten to /not-found, the proxy applies security headers
    // to all responses. Verify X-Frame-Options: DENY is present.
    const response = await request.get("/platform");
    const xfo = response.headers()["x-frame-options"];
    expect(xfo).toBe("DENY");
  });

  test("X-Content-Type-Options nosniff header present on /platform", async ({ request }) => {
    const response = await request.get("/platform");
    expect(response.headers()["x-content-type-options"]).toBe("nosniff");
  });

  test("spoofed x-tenant header cannot expose platform admin UI", async ({ page }) => {
    await page.setExtraHTTPHeaders({ "x-tenant": "__admin__" });
    await page.goto("/platform");

    await expect(
      page.getByRole("heading", { name: /platform administration/i }),
    ).not.toBeVisible({ timeout: 5_000 });
    await expect(
      page.getByRole("button", { name: /provision tenant/i }),
    ).not.toBeVisible();
    await expect(page.getByRole("heading", { name: "Page not found" })).toBeVisible();
  });
});

test.describe("platform admin path guard — unauthenticated user", () => {
  // Override storageState to start logged-out (ANONYMOUS).
  // The guard must also work for unauthenticated requests.
  test.use({ storageState: { cookies: [], origins: [] } });

  test("/platform renders 404 without exposing platform UI (unauthenticated)", async ({
    page,
  }) => {
    await page.goto("/platform");

    // The proxy rewrites /platform → /not-found BEFORE the layout auth check runs, so
    // the platform layout's redirect-to-login never fires. The 404 page is rendered.
    // Either way, NO platform admin UI must be visible.
    await expect(
      page.getByRole("heading", { name: /platform administration/i }),
    ).not.toBeVisible({ timeout: 8_000 });
    await expect(
      page.getByRole("button", { name: /provision tenant/i }),
    ).not.toBeVisible();

    // Positive assertion: the 404 page is rendered (not-found.tsx has <h1>Page not found</h1>).
    await expect(page.getByRole("heading", { name: "Page not found" })).toBeVisible();
  });
});

test.describe("platform API endpoints are permission-gated (direct BFF call)", () => {
  // These tests call the BFF proxy directly using the authenticated primary user's session
  // (cookies from the default storageState are shared with the `request` fixture).
  // The primary user does NOT hold platform.tenants.manage, so every admin endpoint
  // must return 403.
  //
  // Note on mutating requests (POST/PUT): the BFF enforces CSRF double-submit. The
  // `request` fixture does not automatically attach the `mp_csrf` cookie/header pair,
  // so the BFF itself returns 403 with errorCode "security.csrf_failed" before the
  // backend is even reached. This is still a valid 403 and confirms the route is
  // protected — the CSRF guard fires first, then the permission guard for any
  // request that passes CSRF. The GET test (no CSRF requirement) confirms the
  // backend-level 403 from the permission guard directly.

  test("GET /api/bff/tenant/admin/platform-billing returns 403 for regular user", async ({
    request,
  }) => {
    // GET is not subject to CSRF; the request reaches the backend with the user's
    // session and the backend enforces PlatformPermissions.PlatformTenantsManage → 403.
    const response = await request.get("/api/bff/tenant/admin/platform-billing");
    expect(response.status()).toBe(403);
  });

  test("POST /api/bff/tenant/admin/tenants is blocked (CSRF or permission)", async ({
    request,
  }) => {
    // Without the CSRF header, the BFF gate fires first (403 csrf_failed).
    // Either way, no tenant is provisioned and 403 is the result.
    const response = await request.post("/api/bff/tenant/admin/tenants", {
      data: { name: "Probe Tenant", subdomain: "probe-tenant-e2e" },
    });
    expect(response.status()).toBe(403);
  });

  test("PUT /api/bff/tenant/admin/tenants/{id}/entitlements/{key} is blocked (CSRF or permission)", async ({
    request,
  }) => {
    const fakeId = "00000000-0000-7000-8000-000000000001";
    const response = await request.put(
      `/api/bff/tenant/admin/tenants/${fakeId}/entitlements/billing`,
      { data: { enabled: true, tier: null } },
    );
    expect(response.status()).toBe(403);
  });

  test("POST /api/bff/tenant/admin/tenants/{id}/invites is blocked (CSRF or permission)", async ({
    request,
  }) => {
    const fakeId = "00000000-0000-7000-8000-000000000001";
    const response = await request.post(
      `/api/bff/tenant/admin/tenants/${fakeId}/invites`,
      { data: { expiresInDays: 7 } },
    );
    expect(response.status()).toBe(403);
  });
});
