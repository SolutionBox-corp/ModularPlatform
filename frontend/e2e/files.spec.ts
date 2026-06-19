import { test, expect } from "@playwright/test";
import { registerFreshUser } from "./helpers";

// ---------------------------------------------------------------------------
// FILES — /files page
//
// The primary session (shared storageState) is used only for read/non-destructive
// assertions (dropzone hint, a11y, token check). Every upload test registers a fresh
// user so the file table starts empty and tests remain independent and idempotent.
// ---------------------------------------------------------------------------

// ── Helpers ─────────────────────────────────────────────────────────────────

/** Tiny valid text file buffer. Keeps upload tests fast and avoids network overhead. */
function makeTxtFile(content = "hello world"): Buffer {
  return Buffer.from(content, "utf-8");
}

// ── Empty-state / page-structure tests (primary session) ────────────────────
// Note: the primary user accumulates uploads across the suite run, so we cannot
// rely on empty state here. Empty-state must be asserted on a fresh account.

test.describe("Files page — dropzone hints and a11y (primary session)", () => {
  test("dropzone shows allowlist hint and size cap", async ({ page }) => {
    await page.goto("/files");

    // Subtitle should mention allowed types and the size cap.
    // The dropzone renders: FILE_ALLOWED_EXTENSIONS_LABEL + " — up to " + (10MB/1024/1024) + " MB"
    const dropzone = page.getByRole("button", {
      name: /upload a file/i,
    });
    await expect(dropzone).toBeVisible();

    // The subtitle paragraph is inside the dropzone; check for key text.
    await expect(
      page.getByText(/PNG, JPEG, PDF, TXT/i),
    ).toBeVisible();
    await expect(
      page.getByText(/up to 10 MB/i),
    ).toBeVisible();
  });

  test("page heading and dropzone aria-label are accessible", async ({ page }) => {
    await page.goto("/files");

    // H1 heading
    await expect(page.getByRole("heading", { name: "Files", level: 1 })).toBeVisible();

    // The dropzone carries a meaningful aria-label for screen readers.
    const dropzone = page.getByRole("button", {
      name: "Upload a file — drag and drop or click to browse",
    });
    await expect(dropzone).toBeVisible();
    // Confirm aria-disabled is false while not uploading.
    await expect(dropzone).not.toHaveAttribute("aria-disabled", "true");
  });

  test("session token is not accessible in JS storage", async ({ page }) => {
    await page.goto("/files");

    // Tokens live in httpOnly cookies — they must NOT be readable from JS.
    const lsJson = await page.evaluate(() => JSON.stringify(window.localStorage));
    const ssJson = await page.evaluate(() => JSON.stringify(window.sessionStorage));
    const cookie = await page.evaluate(() => document.cookie);

    // No JWT-shaped value in web storage.
    const jwtPattern = /eyJ[A-Za-z0-9_-]+\.[A-Za-z0-9_-]+\.[A-Za-z0-9_-]*/;
    expect(lsJson).not.toMatch(jwtPattern);
    expect(ssJson).not.toMatch(jwtPattern);

    // The httpOnly session cookie is invisible from JS. The only readable cookie
    // should be mp_csrf (the CSRF token) — it must NOT be the full session.
    expect(cookie).not.toMatch(jwtPattern);
    // Sanity: no access/refresh token value should appear in JS-accessible storage.
    // (Next.js dev seeds an unrelated "__next" debug key — that is not a token.)
    expect(lsJson).not.toMatch(/"(access|refresh)Token"|bearer/i);
    expect(ssJson).not.toMatch(/"(access|refresh)Token"|bearer/i);
  });
});

// ── Upload + table tests (fresh user for clean state) ───────────────────────

test.describe("Files page — upload flow (fresh account)", () => {
  test("shows empty state for a fresh account", async ({ page }) => {
    // Fresh user → empty file table.
    await registerFreshUser(page);
    await page.goto("/files");

    // DataTable emptyTitle / emptyDescription
    await expect(page.getByText("No files yet")).toBeVisible();
    await expect(
      page.getByText(/upload a file using the dropzone above/i),
    ).toBeVisible();
  });

  test("upload a txt file and row appears in table", async ({ page }) => {
    await registerFreshUser(page);
    await page.goto("/files");

    // Confirm empty state first.
    await expect(page.getByText("No files yet")).toBeVisible();

    // The hidden file input is sr-only — use setInputFiles directly on it.
    const fileInput = page.locator('input[type="file"]');
    await fileInput.setInputFiles({
      name: "hello.txt",
      mimeType: "text/plain",
      buffer: makeTxtFile("hello.txt"),
    });

    // Success toast should appear.
    await expect(
      page.getByText(/uploaded successfully/i),
    ).toBeVisible({ timeout: 15_000 });

    // The file table row should appear with the filename.
    // "No files yet" should be gone.
    await expect(page.getByText("No files yet")).not.toBeVisible();

    // Row: file name text. Wait for the row to appear after cache invalidation + re-fetch.
    const row = page.getByRole("row", { name: /hello\.txt/i });
    await expect(row).toBeVisible({ timeout: 10_000 });

    // TXT type badge inside the row — scope to the badge element ([data-slot="badge"])
    // to avoid matching the filename span which also contains "hello.txt".
    await expect(row.locator('[data-slot="badge"]').filter({ hasText: "TXT" })).toBeVisible();

    // Download anchor: href must point to /api/bff/files/{uuid} and carry the download attribute.
    const downloadLink = row.getByRole("link", { name: /download hello\.txt/i });
    await expect(downloadLink).toBeVisible();

    const href = await downloadLink.getAttribute("href");
    expect(href).toMatch(/^\/api\/bff\/files\/[0-9a-f-]{36}$/i);

    const downloadAttr = await downloadLink.getAttribute("download");
    expect(downloadAttr).toBe("hello.txt");
  });

  test("client rejects a disallowed MIME type before upload", async ({ page }) => {
    // This test relies on Playwright's setInputFiles accepting an explicit mimeType.
    // The client's validateFile() checks file.type, which the browser derives from the
    // provided mimeType. If the mimeType is spoofed by the test harness without the
    // browser reflecting it in file.type, the test is partial — see catalog FILES-05.
    await registerFreshUser(page);
    await page.goto("/files");

    // Monitor network requests to confirm NO upload request is sent.
    let uploadRequested = false;
    page.on("request", (req) => {
      if (req.url().includes("/api/bff/files") && req.method() === "POST") {
        uploadRequested = true;
      }
    });

    const fileInput = page.locator('input[type="file"]');
    await fileInput.setInputFiles({
      name: "image.gif",
      mimeType: "image/gif",
      buffer: Buffer.from("GIF89a"),
    });

    // The validation toast is synchronous but transient; the DETERMINISTIC signal of a
    // client-side rejection is that no upload POST fired and the file never entered the table.
    await expect(page.getByText(/not allowed/i)).toBeVisible({ timeout: 8_000 });
    await expect(page.getByRole("row", { name: /image\.gif/i })).toHaveCount(0);
    expect(uploadRequested).toBe(false);
  });
});
