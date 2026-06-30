import { test, expect } from "@playwright/test";
import { ANONYMOUS, registerFreshUser } from "./helpers";

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

function extractFileIdFromDownloadHref(href: string | null): string {
  expect(href).toMatch(/^\/api\/bff\/files\/[0-9a-f-]{36}$/i);
  return href!.split("/").at(-1)!;
}

async function uploadTxtAndGetFileId(page: import("@playwright/test").Page, name: string): Promise<string> {
  await page.locator('input[type="file"]').setInputFiles({
    name,
    mimeType: "text/plain",
    buffer: makeTxtFile(name),
  });
  const row = page.getByRole("row", { name: new RegExp(name.replace(".", "\\."), "i") });
  await expect(row).toBeVisible({ timeout: 10_000 });
  await row.getByRole("button", { name: `Actions for ${name}` }).click();
  const downloadLink = page.locator(`[role="menuitem"][download="${name}"]`);
  await expect(downloadLink).toBeVisible();
  const href = await downloadLink.getAttribute("href");
  await page.keyboard.press("Escape");
  return extractFileIdFromDownloadHref(href);
}

async function csrfToken(page: import("@playwright/test").Page): Promise<string> {
  return await page.evaluate(() => {
    const match = document.cookie.match(/(?:^|; )mp_csrf=([^;]+)/);
    return match ? decodeURIComponent(match[1]) : "";
  });
}

test.describe("Files page — unauthenticated", () => {
  test.use(ANONYMOUS);

  test("unauthenticated visit redirects to login", async ({ page }) => {
    await page.goto("/files");

    await expect(page).toHaveURL(/\/login/);
    await expect(page.getByRole("textbox", { name: "Email" })).toBeVisible();
    await expect(page.getByRole("heading", { name: "Files", level: 1 })).toHaveCount(0);
  });
});

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

    // Download lives in the per-row actions menu; it must still be a normal anchor
    // to the BFF proxy endpoint and carry the download attribute.
    await row.getByRole("button", { name: "Actions for hello.txt" }).click();
    const downloadLink = page.locator('[role="menuitem"][download="hello.txt"]');
    await expect(downloadLink).toBeVisible();

    const href = await downloadLink.getAttribute("href");
    extractFileIdFromDownloadHref(href);

    const downloadAttr = await downloadLink.getAttribute("download");
    expect(downloadAttr).toBe("hello.txt");
  });

  test("multiple sequential uploads accumulate in the table", async ({ page }) => {
    await registerFreshUser(page);
    await page.goto("/files");

    await uploadTxtAndGetFileId(page, "a.txt");
    await uploadTxtAndGetFileId(page, "b.txt");

    await expect(page.getByRole("row", { name: /a\.txt/i })).toBeVisible();
    await expect(page.getByRole("row", { name: /b\.txt/i })).toBeVisible();
  });

  test("client rejects a disallowed MIME type before upload", async ({ page }) => {
    // This test relies on Playwright's setInputFiles accepting an explicit mimeType.
    // The client's validateFile() checks file.type, which the browser derives from the
    // provided mimeType. If the mimeType is spoofed by the test harness without the
    // browser reflecting it in file.type, the test is partial — see catalog FILES-05.
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

    // Deterministic client-side rejection signal: no upload POST fired and the file never entered the table.
    await page.waitForTimeout(500);
    await expect(page.getByRole("row", { name: /image\.gif/i })).toHaveCount(0);
    expect(uploadRequested).toBe(false);
  });

  test("another user cannot download a private file", async ({ page }) => {
    await page.goto("/files");
    const fileId = await uploadTxtAndGetFileId(page, "owner-secret.txt");

    await registerFreshUser(page);
    const status = await page.evaluate(async (id) => {
      const res = await fetch(`/api/bff/files/${id}`, { credentials: "same-origin" });
      return res.status;
    }, fileId);

    expect(status).toBe(404);
  });

  test("server rejects a direct disallowed file type upload", async ({ page }) => {
    await page.goto("/files");
    const csrf = await csrfToken(page);

    const result = await page.evaluate(async (token) => {
      const form = new FormData();
      form.append("file", new File(["MZ"], "evil.exe", { type: "application/x-msdownload" }));
      const res = await fetch("/api/bff/files", {
        method: "POST",
        headers: { "x-csrf-token": token },
        body: form,
        credentials: "same-origin",
      });
      return { status: res.status, body: await res.text() };
    }, csrf);

    expect(result.status).toBe(400);
    expect(result.body).toContain("file.content_type.not_allowed");
    await expect(page.getByRole("row", { name: /evil\.exe/i })).toHaveCount(0);
  });

  test("server rejects an oversized direct upload", async ({ page }) => {
    await page.goto("/files");
    const csrf = await csrfToken(page);

    const result = await page.evaluate(async (token) => {
      const form = new FormData();
      form.append(
        "file",
        new File([new Uint8Array(11 * 1024 * 1024)], "big.pdf", { type: "application/pdf" }),
      );
      const res = await fetch("/api/bff/files", {
        method: "POST",
        headers: { "x-csrf-token": token },
        body: form,
        credentials: "same-origin",
      });
      return { status: res.status, body: await res.text() };
    }, csrf);

    expect([400, 413]).toContain(result.status);
    await expect(page.getByRole("row", { name: /big\.pdf/i })).toHaveCount(0);
  });
});
