# Files — Test Scenario Catalog

The Files page (`/files`) lets authenticated users upload, list, and download private files.
Backend security: a content-type allowlist (PNG, JPEG, PDF, TXT), a 10 MB hard size cap, and
a server-generated storage key. Files are RLS-isolated: each user sees only their own files.
Client-side pre-validation (`schema.ts`) mirrors the allowlist and cap so the UI rejects
disallowed types / oversize files before the round-trip. The dropzone shows the allowlist and
size hint in a subtitle; the file table shows empty state ("No files yet") before any upload.

---

## Scenarios

- **FILES-01** — Empty state is shown for a new account
  - Given: a freshly registered user with no uploaded files
  - When: they navigate to `/files`
  - Then: the file table shows "No files yet" and "Upload a file using the dropzone above."
  - Priority: P0 · Type: happy · Automated: yes (e2e: "shows empty state for a fresh account")

- **FILES-02** — Dropzone renders with allowlist hint and size cap
  - Given: authenticated user on `/files`
  - When: the page loads
  - Then: the dropzone shows "PNG, JPEG, PDF, TXT" and "up to 10 MB" in its subtitle text
  - Priority: P1 · Type: happy · Automated: yes (e2e: "dropzone shows allowlist hint and size cap")

- **FILES-03** — Successful upload of a small allowed text file (TXT)
  - Given: a fresh authenticated user on `/files` (empty table)
  - When: they upload a small `hello.txt` buffer via the hidden file input
  - Then: a success toast "… uploaded successfully." appears, the file table row for `hello.txt`
    is visible, the row shows the file name, "TXT" type badge, and a download link; the empty
    state is no longer shown
  - Priority: P0 · Type: happy · Automated: yes (e2e: "upload a txt file and row appears in table")

- **FILES-04** — Download link targets the BFF proxy endpoint
  - Given: a file has been uploaded and its row is in the table
  - When: the user inspects the download anchor for that file
  - Then: the `href` attribute is `/api/bff/files/{id}` (a normal `<a>` with `download`, NOT
    a JS-triggered fetch); the `download` attribute equals the original filename
  - Priority: P1 · Type: happy · Automated: yes (e2e: "upload a txt file and row appears in table")

- **FILES-05** — Client-side rejection of a disallowed content type
  - Given: authenticated user on `/files`
  - When: they attempt to upload a `.gif` file (type `image/gif`, not in the allowlist)
  - Then: the client validates synchronously and a toast error appears containing "not allowed";
    no network request is made to the upload API; the file table is unchanged
  - Priority: P1 · Type: error · Automated: partial
    (browser MIME detection for `setInputFiles` uses the extension — see GAPS below)

- **FILES-06** — Client-side rejection of an oversized file (> 10 MB)
  - Given: authenticated user on `/files`
  - When: they attempt to upload a file whose size exceeds 10 MB
  - Then: a toast error appears containing "too large" / "10 MB"; no upload request is sent
  - Priority: P1 · Type: error · Automated: partial (constructing a >10 MB buffer in-memory
    works but is slow; mocking is preferred — see GAPS below)

- **FILES-07** — Unauthenticated access redirects to login
  - Given: a user who is not logged in
  - When: they navigate to `/files`
  - Then: they are redirected to `/login` (or a 401/redirect) without seeing any file data
  - Priority: P0 · Type: security · Automated: manual (requires ANONYMOUS context + redirect assertion)

- **FILES-08** — Token is never exposed in JS storage
  - Given: authenticated user on `/files`
  - When: the page is loaded
  - Then: `localStorage`, `sessionStorage`, and `document.cookie` (as seen from JS) contain no
    session token; the session cookie is httpOnly (not accessible via `document.cookie`)
  - Priority: P1 · Type: security · Automated: yes (e2e: "session token is not accessible in JS storage")

- **FILES-09** — File isolation: a user cannot download another user's file
  - Given: user A has uploaded a file with a known ID; user B is logged in
  - When: user B attempts to GET `/api/bff/files/{userA_file_id}`
  - Then: the response is 404 (RLS + explicit ownership check in the handler)
  - Priority: P0 · Type: security · Automated: manual (requires two user sessions)

- **FILES-10** — Page heading and subtitle are accessible
  - Given: authenticated user navigates to `/files`
  - When: the page fully renders
  - Then: there is an `<h1>` with text "Files"; the subtitle "Upload and manage your files…"
    is visible; the dropzone has `aria-label="Upload a file — drag and drop or click to browse"`
  - Priority: P1 · Type: a11y · Automated: yes (e2e: "page heading and dropzone aria-label are accessible")

- **FILES-11** — Dropzone is keyboard-activatable (Enter/Space)
  - Given: authenticated user on `/files`
  - When: they focus the dropzone (role="button") and press Enter or Space
  - Then: the hidden file input is triggered (no error, no crash); uploading remains functional
  - Priority: P2 · Type: a11y · Automated: manual (OS file dialog cannot be driven by Playwright
    without `setInputFiles` on the input directly)

- **FILES-12** — Multiple sequential uploads accumulate in the table
  - Given: fresh user on `/files`
  - When: they upload a first `a.txt`, then a second `b.txt`
  - Then: both rows appear in the file table
  - Priority: P2 · Type: happy · Automated: manual (smoke-level; PRIMARY user state persists
    across tests so a fresh user would need fresh registration per test — cost vs coverage)

- **FILES-13** — Backend rejects disallowed type (server-side enforcement)
  - Given: authenticated user makes a direct POST to `/api/bff/files` with an `.exe` file
  - When: the request reaches the backend
  - Then: the API returns a 422 with error code `file.content_type.not_allowed`
  - Priority: P1 · Type: security · Automated: manual (requires direct API call, not UI flow)

- **FILES-14** — Backend rejects oversize file (server-side body size limit)
  - Given: authenticated user sends a POST to `/api/bff/files` with a body > 10 MB
  - When: the request reaches Kestrel
  - Then: the request is rejected before it reaches the handler (413 or 400)
  - Priority: P1 · Type: security · Automated: manual (requires direct API call with large body)
