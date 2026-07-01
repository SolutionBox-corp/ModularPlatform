import { test, expect } from "@playwright/test";
import { ANONYMOUS, registerFreshUser, uniqueEmail } from "./helpers";

const COOKIE_CONSENT_GRANT_PATH = /\/api\/bff\/gdpr\/consents\/grant$/;
const COOKIE_CONSENT_WITHDRAW_PATH = /\/api\/bff\/gdpr\/consents\/withdraw$/;

// ---------------------------------------------------------------------------
// Privacy / GDPR page — /account/privacy
// ---------------------------------------------------------------------------
// All destructive tests (erase account, consent writes) use a FRESH user so
// the shared primary session is never corrupted. Read-only checks reuse the
// primary session (default storageState applied by the chromium project).
// ---------------------------------------------------------------------------

// ---------------------------------------------------------------------------
// SECTION A: Unauthenticated redirect
// ---------------------------------------------------------------------------

test.describe("Unauthenticated redirect", () => {
  test.use(ANONYMOUS);

  // PRIV-08 + PRIV-15 + PRIV-24
  test("unauthenticated access to privacy page redirects to login", async ({ page }) => {
    await page.goto("/account/privacy");
    await expect(page).toHaveURL(/\/login/, { timeout: 15_000 });
  });
});

// ---------------------------------------------------------------------------
// SECTION B: Page structure (shared primary session — non-destructive)
// ---------------------------------------------------------------------------

test.describe("Privacy page structure", () => {
  // PRIV-01
  test("Privacy page renders consent toggles", async ({ page }) => {
    await page.goto("/account/privacy");
    await expect(page.getByRole("heading", { name: /consent preferences/i })).toBeVisible();
    await expect(page.getByRole("switch", { name: /product news & offers/i })).toBeVisible();
    // The orphaned `analytics` + `third_party_sharing` toggles were removed (audit follow-up #5 —
    // nothing in the app reads them yet). Only the real marketing-emails consent remains.
    await expect(page.getByRole("switch", { name: /analytics/i })).toHaveCount(0);
    await expect(page.getByRole("switch", { name: /third-party sharing/i })).toHaveCount(0);
  });

  // PRIV-27
  test("Privacy page renders consent history section", async ({ page }) => {
    await page.goto("/account/privacy");
    await expect(page.getByRole("heading", { name: /consent history/i })).toBeVisible();
    await expect(
      page.getByText(/every consent decision|no consent history/i).first(),
    ).toBeVisible();
  });

  // PRIV-11
  test("Privacy page renders export section", async ({ page }) => {
    await page.goto("/account/privacy");
    await expect(page.getByRole("heading", { name: /export your data/i })).toBeVisible();
    await expect(page.getByRole("button", { name: /download my data/i })).toBeVisible();
  });

  // PRIV-16 (open only, no destructive action)
  test("erase dialog opens on trigger click", async ({ page }) => {
    await page.goto("/account/privacy");
    await page.getByRole("button", { name: /delete my account/i }).click();
    await expect(page.getByRole("dialog")).toBeVisible();
    await expect(
      page.getByRole("heading", { name: /delete account permanently/i }),
    ).toBeVisible();
  });

  // PRIV-25
  test("erase dialog shows irreversibility warning", async ({ page }) => {
    await page.goto("/account/privacy");
    await page.getByRole("button", { name: /delete my account/i }).click();
    const dialog = page.getByRole("dialog");
    await expect(dialog).toBeVisible();
    // The dialog should mention what will be erased and that it cannot be undone.
    await expect(dialog).toContainText(/profile/i);
    await expect(dialog).toContainText(/cannot be undone/i);
  });
});

// ---------------------------------------------------------------------------
// SECTION C2: Cookie banner consent persistence
// ---------------------------------------------------------------------------

test.describe("Cookie banner consent persistence", () => {
  // PRIV-29
  test("cookie banner accept all persists analytics and marketing cookie grants", async ({
    page,
  }) => {
    const posted: unknown[] = [];
    await resetCookieBanner(page);
    await page.route(COOKIE_CONSENT_GRANT_PATH, async (route) => {
      posted.push(route.request().postDataJSON());
      await route.fulfill({
        status: 200,
        contentType: "application/json",
        body: JSON.stringify({ data: { consentRecordId: crypto.randomUUID() } }),
      });
    });

    await page.goto("/account/privacy");
    await page.getByRole("button", { name: /accept all/i }).click();

    await expect
      .poll(() => posted.map((body) => consentTypeFromBody(body)).sort())
      .toEqual(["cookie_analytics", "cookie_marketing"]);
    for (const body of posted) {
      expect(policyVersionFromBody(body)).toBe("2026-06-20");
    }
  });

  // PRIV-30
  test("cookie banner necessary only persists analytics and marketing cookie withdrawals", async ({
    page,
  }) => {
    const posted: unknown[] = [];
    await resetCookieBanner(page);
    await page.route(COOKIE_CONSENT_WITHDRAW_PATH, async (route) => {
      posted.push(route.request().postDataJSON());
      await route.fulfill({
        status: 200,
        contentType: "application/json",
        body: JSON.stringify({ data: { consentRecordId: crypto.randomUUID() } }),
      });
    });

    await page.goto("/account/privacy");
    await page.getByRole("button", { name: /necessary only/i }).click();

    await expect
      .poll(() => posted.map((body) => consentTypeFromBody(body)).sort())
      .toEqual(["cookie_analytics", "cookie_marketing"]);
    for (const body of posted) {
      expect(policyVersionFromBody(body)).toBe("2026-06-20");
    }
  });
});

test.describe("Cookie banner anonymous visitor", () => {
  test.use(ANONYMOUS);

  // PRIV-31
  test("anonymous cookie consent persistence failure does not redirect to login", async ({
    page,
  }) => {
    await resetCookieBanner(page);
    await page.route(/\/api\/bff\/gdpr\/consents\/(grant|withdraw)$/, async (route) => {
      await route.fulfill({
        status: 401,
        contentType: "application/problem+json",
        body: JSON.stringify({ status: 401, errorCode: "auth.required" }),
      });
    });

    await page.goto("/login");
    await page.getByRole("button", { name: /accept all/i }).click();

    await expect(page).toHaveURL(/\/login$/);
    await expect(page.getByRole("button", { name: /accept all/i })).toHaveCount(0);
  });
});

// ---------------------------------------------------------------------------
// SECTION C: Confirm gate (dialog) — shared primary session, no actual erase
// ---------------------------------------------------------------------------

test.describe("Erase account dialog confirm gate", () => {
  // PRIV-17
  test("erase confirm button disabled before correct phrase", async ({ page }) => {
    await page.goto("/account/privacy");
    await page.getByRole("button", { name: /delete my account/i }).click();
    const dialog = page.getByRole("dialog");
    await expect(dialog).toBeVisible();
    const submitBtn = dialog.getByRole("button", { name: /permanently delete my account/i });
    await expect(submitBtn).toBeDisabled();
  });

  // PRIV-18
  test("erase confirm button disabled for partial phrase", async ({ page }) => {
    await page.goto("/account/privacy");
    await page.getByRole("button", { name: /delete my account/i }).click();
    const dialog = page.getByRole("dialog");
    await expect(dialog).toBeVisible();
    await dialog.getByRole("textbox").fill("delete");
    const submitBtn = dialog.getByRole("button", { name: /permanently delete my account/i });
    await expect(submitBtn).toBeDisabled();
  });

  // PRIV-21 — phrase with leading/trailing spaces and mixed case (UI trims + lowercases)
  test("erase confirm button enabled for phrase with leading/trailing spaces and uppercase", async ({
    page,
  }) => {
    await page.goto("/account/privacy");
    await page.getByRole("button", { name: /delete my account/i }).click();
    const dialog = page.getByRole("dialog");
    await expect(dialog).toBeVisible();
    await dialog.getByRole("textbox").fill("  Delete My Account  ");
    const submitBtn = dialog.getByRole("button", { name: /permanently delete my account/i });
    await expect(submitBtn).toBeEnabled();
  });

  // PRIV-19 — cancel resets input and closes dialog
  test("erase dialog cancel clears input and closes", async ({ page }) => {
    await page.goto("/account/privacy");
    await page.getByRole("button", { name: /delete my account/i }).click();
    const dialog = page.getByRole("dialog");
    await expect(dialog).toBeVisible();
    // Type the phrase so we can verify it is cleared on close
    const input = dialog.getByRole("textbox");
    await input.fill("delete my account");
    const submitBtn = dialog.getByRole("button", { name: /permanently delete my account/i });
    await expect(submitBtn).toBeEnabled();
    // Close via Escape — the dialog component resets confirmValue via handleOpenChange
    await page.keyboard.press("Escape");
    await expect(dialog).not.toBeVisible({ timeout: 5_000 });
    // Reopen and verify the input is empty
    await page.getByRole("button", { name: /delete my account/i }).click();
    await expect(dialog).toBeVisible();
    await expect(dialog.getByRole("textbox")).toHaveValue("");
    // Close cleanly
    await page.keyboard.press("Escape");
  });

  // PRIV-26 — autocomplete disabled
  test("erase confirm input has autocomplete=off", async ({ page }) => {
    await page.goto("/account/privacy");
    await page.getByRole("button", { name: /delete my account/i }).click();
    const dialog = page.getByRole("dialog");
    await expect(dialog).toBeVisible();
    const input = dialog.getByRole("textbox");
    await expect(input).toHaveAttribute("autocomplete", "off");
    await page.keyboard.press("Escape");
  });
});

// ---------------------------------------------------------------------------
// SECTION D: Consent toggle persistence — fresh user (write operations)
// ---------------------------------------------------------------------------

test.describe("Consent toggles — fresh user", () => {
  // PRIV-02 — grant marketing consent and verify it survives reload
  test("toggle marketing consent — grant persists after reload", async ({ page }) => {
    await registerFreshUser(page);
    await page.goto("/account/privacy");

    const marketingSwitch = page.getByRole("switch", { name: /product news & offers/i });
    await expect(marketingSwitch).toBeVisible();

    // The fresh user has no consent history, so the switch starts unchecked.
    await expect(marketingSwitch).toHaveAttribute("aria-checked", "false");

    await marketingSwitch.click();

    // After grant the switch should flip and a success toast should appear.
    await expect(marketingSwitch).toHaveAttribute("aria-checked", "true");
    await expect(page.getByText(/consent granted/i)).toBeVisible();

    // Reload and verify the state is persisted.
    await page.reload();
    await expect(page.getByRole("switch", { name: /product news & offers/i })).toHaveAttribute(
      "aria-checked",
      "true",
    );
  });

  // PRIV-03 — grant marketing consent then withdraw and verify withdrawal persists
  test("toggle marketing consent — withdraw persists after reload", async ({ page }) => {
    await registerFreshUser(page);
    await page.goto("/account/privacy");

    const consentSwitch = page.getByRole("switch", { name: /product news & offers/i });
    await expect(consentSwitch).toBeVisible();

    // Grant first so we have something to withdraw.
    await consentSwitch.click();
    await expect(consentSwitch).toHaveAttribute("aria-checked", "true");
    await expect(page.getByText(/consent granted/i)).toBeVisible();

    // Now withdraw.
    await consentSwitch.click();
    await expect(consentSwitch).toHaveAttribute("aria-checked", "false");
    await expect(page.getByText(/consent withdrawn/i)).toBeVisible();

    // Reload and verify withdrawal persists.
    await page.reload();
    await expect(page.getByRole("switch", { name: /product news & offers/i })).toHaveAttribute(
      "aria-checked",
      "false",
    );
  });

  // PRIV-04 — toggle marketing consent twice, verify settles at withdrawn
  test("toggle marketing consent twice settles at withdrawn", async ({ page }) => {
    await registerFreshUser(page);
    await page.goto("/account/privacy");

    const consentSwitch = page.getByRole("switch", { name: /product news & offers/i });
    await expect(consentSwitch).toBeVisible();

    // Grant
    await consentSwitch.click();
    await expect(consentSwitch).toHaveAttribute("aria-checked", "true");
    await expect(page.getByText(/consent granted/i)).toBeVisible();

    // Withdraw
    await consentSwitch.click();
    await expect(consentSwitch).toHaveAttribute("aria-checked", "false");
    await expect(page.getByText(/consent withdrawn/i)).toBeVisible();

    // Reload and verify final state is withdrawn
    await page.reload();
    await expect(page.getByRole("switch", { name: /product news & offers/i })).toHaveAttribute(
      "aria-checked",
      "false",
    );
  });
});

// ---------------------------------------------------------------------------
// SECTION E: Export data — fresh user
// ---------------------------------------------------------------------------

test.describe("Export data — fresh user", () => {
  // PRIV-12 — clicking export triggers a synchronous backend call + browser Blob download,
  // then shows the "Downloaded" indicator and a success toast.
  //
  // The backend GET /gdpr/me/export is synchronous (200 + JSON body), NOT a 202/operation flow.
  // ExportDataFlow triggers a programmatic Blob URL download (no browser "download" event fires
  // in Playwright for blob: URLs created via anchor.click()), so we do NOT use waitForEvent("download").
  // Instead: click → wait for the success toast → wait for the "Downloaded" indicator.
  test("export — button transitions to downloaded state", async ({ page }) => {
    await registerFreshUser(page);
    await page.goto("/account/privacy");

    const exportBtn = page.getByRole("button", { name: /download my data/i });
    await expect(exportBtn).toBeVisible();

    await exportBtn.click();

    // After the synchronous export resolves: the success toast appears.
    await expect(page.getByText(/your data export is ready/i)).toBeVisible({ timeout: 20_000 });

    // The "Downloaded" span appears beside the button once exported=true and isPending=false.
    // exact:true avoids also matching the toast text "...has been downloaded.".
    await expect(page.getByText("Downloaded", { exact: true })).toBeVisible({ timeout: 10_000 });

    // Button must still be enabled after completion (not permanently disabled).
    await expect(exportBtn).toBeEnabled({ timeout: 5_000 });
  });
});

// ---------------------------------------------------------------------------
// SECTION F: Erase account full flow — fresh throwaway user
// ---------------------------------------------------------------------------

test.describe("Erase account — fresh user", () => {
  // PRIV-20 — full erase flow: type phrase, confirm, redirect to /login
  test("erase account full flow redirects to login", async ({ page }) => {
    // Register a dedicated throwaway user for this destructive test.
    await registerFreshUser(page, {
      email: uniqueEmail("e2e-erase"),
      displayName: "Erase Me",
    });
    await page.goto("/account/privacy");

    // Open the dialog.
    await page.getByRole("button", { name: /delete my account/i }).click();
    const dialog = page.getByRole("dialog");
    await expect(dialog).toBeVisible();

    // The submit button should be disabled before typing.
    const submitBtn = dialog.getByRole("button", { name: /permanently delete my account/i });
    await expect(submitBtn).toBeDisabled();

    // Type the exact confirmation phrase.
    await dialog.getByRole("textbox").fill("delete my account");
    await expect(submitBtn).toBeEnabled();

    // Submit.
    await submitBtn.click();

    // A success toast should appear and the user is redirected to /login?reason=erased.
    await expect(page).toHaveURL(/\/login/, { timeout: 20_000 });

    // Verify the erasure reason is in the URL.
    await expect(page).toHaveURL(/reason=erased/, { timeout: 5_000 });
  });
});

async function resetCookieBanner(page: import("@playwright/test").Page): Promise<void> {
  await page.context().clearCookies({ name: "cc_cookie" });
  await page.addInitScript(() => {
    document.cookie = "cc_cookie=; Max-Age=0; path=/; SameSite=Lax";
  });
}

function consentTypeFromBody(body: unknown): string | undefined {
  return body && typeof body === "object" && "consentType" in body
    ? String(body.consentType)
    : undefined;
}

function policyVersionFromBody(body: unknown): string | undefined {
  return body && typeof body === "object" && "policyVersion" in body
    ? String(body.policyVersion)
    : undefined;
}
