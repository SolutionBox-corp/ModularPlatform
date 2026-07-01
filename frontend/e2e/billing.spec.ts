import { test, expect } from "@playwright/test";
import { ANONYMOUS } from "./helpers";

// ---------------------------------------------------------------------------
// Most tests reuse the shared primary storageState (authenticated, billing-entitled).
// The unauthenticated + token-storage tests opt out via ANONYMOUS.
// ---------------------------------------------------------------------------

test.describe("Billing page — layout and page structure", () => {
  test("page heading and section headings are present", async ({ page }) => {
    await page.goto("/billing");

    await expect(
      page.getByRole("heading", { name: "Billing", level: 1 }),
    ).toBeVisible();
    await expect(
      page.getByRole("heading", { name: "Buy credits", level: 2 }),
    ).toBeVisible();
    await expect(
      page.getByRole("heading", { name: "Promo code", level: 2 }),
    ).toBeVisible();
    await expect(
      page.getByRole("heading", { name: "Credit balance", level: 2 }),
    ).toBeVisible();
    await expect(
      page.getByRole("heading", { name: "Choose a plan", level: 2 }),
    ).toBeVisible();
    await expect(
      page.getByRole("heading", { name: "Workspace platform plan", level: 2 }),
    ).toBeVisible();
    await expect(
      page.getByRole("heading", { name: "Transaction history", level: 2 }),
    ).toBeVisible();
    await expect(
      page.getByRole("button", { name: "Manage billing & invoices" }),
    ).toBeVisible();
  });
});

test.describe("Billing page — balance card", () => {
  /**
   * BILL-01: Balance card renders credit values.
   * Fresh tenants get a credit account provisioned by the UserRegistered → Billing handler,
   * so the shared primary user will always have a CreditBalanceResponse with numeric values.
   * We assert on the visible card title, subtitle, and the "cr." suffix from MoneyAmount.
   */
  test("balance card renders credit values", async ({ page }) => {
    await page.goto("/billing");

    // CardTitle renders as a styled <div>, not a heading role — assert the text content directly
    await expect(page.getByText("Credits", { exact: true }).first()).toBeVisible();

    // Subtitle text
    await expect(page.getByText("Available balance")).toBeVisible();

    // MoneyAmount renders integers as "N cr." — assert the suffix is present at least once
    // (the card shows available and "of N cr. posted")
    const crSuffix = page.locator("text=cr.").first();
    await expect(crSuffix).toBeVisible();

    // "posted" label is visible
    await expect(page.getByText(/posted/i).first()).toBeVisible();
  });
});

test.describe("Billing page — credit summary table", () => {
  /**
   * BILL-03: Credit summary table shows Posted, Available, Held rows.
   */
  test("credit summary table shows Posted Available Held rows", async ({
    page,
  }) => {
    await page.goto("/billing");

    // Wait for the credit balance section
    await expect(
      page.getByRole("heading", { name: "Credit balance", level: 2 }),
    ).toBeVisible();

    // The DataTable renders rows with cell text matching the three category labels
    await expect(page.getByText("Posted", { exact: true })).toBeVisible();
    await expect(page.getByText("Available", { exact: true })).toBeVisible();
    await expect(page.getByText("Held / pending", { exact: true })).toBeVisible();

    // Each row must contain a credit value ("N cr.") — locate all MoneyAmount spans
    // inside the Credit balance section and confirm at least 3 are present
    const creditBalanceSection = page
      .locator("section")
      .filter({ has: page.getByRole("heading", { name: "Credit balance" }) });
    const crValues = creditBalanceSection.locator('[aria-label*="cr."]');
    await expect(crValues).toHaveCount(3);
  });
});

test.describe("Billing page — packages grid", () => {
  /**
   * BILL-05: PackagesGrid shows empty state when the backend returns no packages.
   * In the default dev/test environment no packages are seeded, so the shared primary
   * user will see the empty state.
   */
  test("packages grid shows empty state when no packages", async ({ page }) => {
    await page.goto("/billing");

    await expect(
      page.getByRole("heading", { name: "Buy credits", level: 2 }),
    ).toBeVisible();

    // Wait for the loading skeletons to clear — either the empty state or a package card
    // must appear. Use web-first assertions (toBeVisible with auto-retry) via a race.
    const emptyTitle = page.getByText("No packages available", { exact: true });
    const buyNowButton = page.getByRole("button", { name: "Buy now" }).first();

    // Wait up to 10 s for one of the two settled states to appear
    await Promise.race([
      emptyTitle.waitFor({ state: "visible", timeout: 10_000 }),
      buyNowButton.waitFor({ state: "visible", timeout: 10_000 }),
    ]).catch(() => {
      // Neither appeared — will fail on the assertion below
    });

    const emptyVisible = await emptyTitle.isVisible().catch(() => false);
    const hasPackages = await buyNowButton.isVisible().catch(() => false);

    // One of the two states must be true — the grid never shows neither
    expect(emptyVisible || hasPackages, "Expected either empty state or package cards").toBe(true);

    if (emptyVisible) {
      await expect(
        page.getByText("Credit packages will appear here once configured."),
      ).toBeVisible();
    }
  });
});

test.describe("Billing page — subscription card", () => {
  /**
   * BILL-07: Subscription card shows "No active subscription." for a new user.
   * The shared primary user has never subscribed, so the API returns 404 → null → empty state.
   */
  test("subscription card shows no subscription empty state", async ({
    page,
  }) => {
    await page.goto("/billing");

    // CardTitle renders as a styled <div> not a heading role — scope to the subscription
    // card by finding the "No active subscription." paragraph which is unique to that card.
    await expect(page.getByText("No active subscription.")).toBeVisible();

    // Confirm the card title text is also present
    await expect(page.getByText("Subscription", { exact: true }).first()).toBeVisible();

    // "View plans" link must still be present
    await expect(page.getByRole("link", { name: "View plans" })).toBeVisible();
  });
});

test.describe("Billing page — subscription and platform plans", () => {
  test("subscription plans section reaches a settled state", async ({ page }) => {
    await page.goto("/billing");

    await expect(
      page.getByRole("heading", { name: "Choose a plan", level: 2 }),
    ).toBeVisible();

    const empty = page.getByText("No plans available", { exact: true });
    const subscribe = page.getByRole("button", { name: "Subscribe" }).first();

    await Promise.race([
      empty.waitFor({ state: "visible", timeout: 10_000 }),
      subscribe.waitFor({ state: "visible", timeout: 10_000 }),
    ]).catch(() => {
      // The assertion below will report the failed settled state.
    });

    const emptyVisible = await empty.isVisible().catch(() => false);
    const hasSubscribe = await subscribe.isVisible().catch(() => false);
    expect(emptyVisible || hasSubscribe, "Expected either empty subscription plans or Subscribe buttons").toBe(true);
  });

  test("platform plan checkout section reaches a settled state", async ({ page }) => {
    await page.goto("/billing");

    await expect(
      page.getByRole("heading", { name: "Workspace platform plan", level: 2 }),
    ).toBeVisible();

    const empty = page.getByText("No platform plans available", { exact: true });
    const upgrade = page.getByRole("button", { name: "Upgrade" }).first();
    const current = page.getByRole("button", { name: "Current plan" }).first();

    await Promise.race([
      empty.waitFor({ state: "visible", timeout: 10_000 }),
      upgrade.waitFor({ state: "visible", timeout: 10_000 }),
      current.waitFor({ state: "visible", timeout: 10_000 }),
    ]).catch(() => {
      // The assertion below will report the failed settled state.
    });

    const emptyVisible = await empty.isVisible().catch(() => false);
    const hasUpgrade = await upgrade.isVisible().catch(() => false);
    const hasCurrent = await current.isVisible().catch(() => false);
    expect(
      emptyVisible || hasUpgrade || hasCurrent,
      "Expected either empty platform plans or platform plan buttons",
    ).toBe(true);
  });
});

test.describe("Billing checkout return pages", () => {
  test("success page without purchase id shows missing-purchase state", async ({ page }) => {
    await page.goto("/billing/success");

    await expect(page.getByText("Purchase id missing")).toBeVisible();
    await expect(
      page.getByText(/No purchase id was found/i),
    ).toBeVisible();
  });

  test("cancel page clears pending purchase context", async ({ page }) => {
    await page.goto("/billing");
    await page.evaluate(() => {
      sessionStorage.setItem("billing:lastPurchaseId", "00000000-0000-7000-8000-000000000001");
    });

    await page.goto("/billing/cancel");

    await expect(
      page.getByRole("heading", { name: "Checkout canceled" }),
    ).toBeVisible();
    await expect(page.getByRole("link", { name: "Back to billing" })).toBeVisible();
    await expect
      .poll(() => page.evaluate(() => sessionStorage.getItem("billing:lastPurchaseId")))
      .toBeNull();
  });
});

test.describe("Billing page — promo code", () => {
  /**
   * BILL-10: Submitting the promo form with an empty code shows client validation error.
   */
  test("promo code empty submit shows client validation error", async ({
    page,
  }) => {
    await page.goto("/billing");

    // Wait for the promo input to be ready, then scroll it into view
    const promoInput = page.getByLabel("Promo code");
    await promoInput.waitFor({ state: "visible" });

    // Click Apply without filling in anything
    await page.getByRole("button", { name: "Apply", exact: true }).click();

    // Client-side validation message from promoCodeSchema
    // Re-query after click to get fresh DOM reference
    await expect(page.getByText("Enter a promo code.")).toBeVisible();

    // No network request should have been made — the XCircle error banner should NOT appear
    // (it only appears after a network round-trip error)
    await expect(
      page.getByText("Invalid or expired promo code."),
    ).not.toBeVisible();
  });

  /**
   * BILL-11: Code longer than 64 chars triggers client-side length error.
   */
  test("promo code too-long input shows client validation error", async ({
    page,
  }) => {
    await page.goto("/billing");

    // Wait for the promo input to be ready, then scroll it into view
    const promoInput = page.getByLabel("Promo code");
    await promoInput.waitFor({ state: "visible" });

    const tooLong = "A".repeat(65);
    await promoInput.fill(tooLong);
    await page.getByRole("button", { name: "Apply", exact: true }).click();

    await expect(page.getByText("Code is too long.")).toBeVisible();
  });

  /**
   * BILL-12: Submitting a code that the backend does not recognise shows the error banner.
   * The promo-codes endpoint returns a 404/error for unknown codes, which the
   * billingQueries.promoCode() query (retry:false) propagates as isError=true.
   */
  test("promo code invalid code shows error feedback", async ({ page }) => {
    await page.goto("/billing");

    // Wait for the promo input to be ready, then scroll it into view
    const promoInput = page.getByLabel("Promo code");
    await promoInput.waitFor({ state: "visible" });

    // Enter a code that is syntactically valid but does not exist in Stripe
    await promoInput.fill("INVALID-E2E-CODE");
    await page.getByRole("button", { name: "Apply", exact: true }).click();

    // The error banner must appear (XCircleIcon + text) — re-query after re-render
    await expect(
      page.getByText("Invalid or expired promo code."),
    ).toBeVisible();

    // The success panel must NOT appear
    const successPanel = page.locator('[data-slot="card"]').filter({
      hasText: "% off",
    });
    await expect(successPanel).not.toBeVisible();
  });
});

test.describe("Billing page — security", () => {
  /**
   * BILL-16: Unauthenticated access to /billing is redirected to /login.
   */
  test.use(ANONYMOUS);

  test("unauthenticated user is redirected from billing to login", async ({
    page,
  }) => {
    await page.goto("/billing");
    // Middleware / layout guard should redirect before billing content renders
    await expect(page).toHaveURL(/\/login/, { timeout: 15_000 });

    // The billing page heading must NOT be present
    await expect(
      page.getByRole("heading", { name: "Billing", level: 1 }),
    ).not.toBeVisible();
  });
});

test.describe("Billing page — token storage security", () => {
  /**
   * BILL-17: Session token must NOT be accessible from JavaScript.
   * The app uses httpOnly encrypted cookies; tokens must never appear in
   * localStorage, sessionStorage, or the JS-readable cookie string.
   */
  test("session token is not accessible from JavaScript", async ({ page }) => {
    await page.goto("/billing");

    // Wait for the page to be hydrated (balance card visible)
    await expect(page.getByRole("heading", { name: "Billing", level: 1 })).toBeVisible();

    const { ls, ss, cookieStr } = await page.evaluate(() => {
      // Serialize only app-owned keys — skip Next.js dev internals like __next_debug_channel
      // whose opaque base64 payload coincidentally matches token-ish substrings.
      const dump = (s: Storage) => {
        const out: Record<string, string> = {};
        for (let i = 0; i < s.length; i++) {
          const k = s.key(i)!;
          if (!k.startsWith("__next")) out[k] = s.getItem(k) ?? "";
        }
        return JSON.stringify(out);
      };
      return {
        ls: dump(window.localStorage),
        ss: dump(window.sessionStorage),
        cookieStr: document.cookie,
      };
    });

    // localStorage and sessionStorage should not contain auth tokens.
    expect(ls).not.toMatch(/access_token|refresh_token|jwt|bearer/i);
    expect(ss).not.toMatch(/access_token|refresh_token|jwt|bearer/i);

    // The JS-readable cookie string should not contain the session cookie.
    // The session cookie is httpOnly so it must be absent here.
    // Only the CSRF token (mp_csrf) may be readable.
    expect(cookieStr).not.toMatch(/mp_session|mp_refresh|access_token|refresh_token/i);
  });
});
