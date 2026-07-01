# Notifications — Test Scenario Catalog

Area: `/notifications` page (full paged feed) + the `RecentNotifications` card on the dashboard (`/`).

The in-app notification feed is populated by the Worker via the outbox. A fresh registration
triggers a `UserRegisteredIntegrationEvent`, which `SendWelcomeHandler` converts into a
`SendNotificationCommand` (templateKey `welcome`, channels `["email","inapp"]`). The welcome
notification therefore appears in a fresh user's feed automatically.

Endpoint surface:
- `GET /v1/notifications/me?page=&pageSize=&unreadOnly=` — paged feed, newest-first, user-scoped via JWT
- `GET /v1/notifications/me/unread-count` — unread badge counter, user-scoped via JWT
- `POST /v1/notifications/{id}/read` — stamps `ReadAt`; idempotent (no-op if already read)
- `POST /v1/notifications/me/read-all` — stamps all unread rows for the caller; idempotent when nothing is unread
- `GET /v1/notifications/me/preferences` / `PUT /v1/notifications/me/preferences/{channel}` — per-user channel preferences; in-app is required and not configurable

---

## Scenarios

### Happy-path feed

- **NOTIF-01** — **Welcome notification appears unread for a fresh user**
  - Given: a brand-new user has just registered (Worker has processed the welcome event)
  - When: they navigate to `/notifications`
  - Then: the feed shows at least one item with title "Welcome to the platform!", a "New" badge, and a
    "Mark as read" button; a formatted timestamp is visible; the item row has a highlighted background
  - Priority: P0 · Type: happy · Automated: yes (e2e: `shows welcome notification unread`)

- **NOTIF-02** — **Feed title and description render**
  - Given: an authenticated user on `/notifications`
  - When: the page loads
  - Then: an `<h1>` with text "Notifications" and a subtitle "Your recent activity and system messages." are visible
  - Priority: P1 · Type: happy · Automated: yes (e2e: `notifications page heading renders`)

- **NOTIF-03** — **"New" badge disappears after Mark as read**
  - Given: a fresh user's welcome notification is unread (badge "New" visible, checkmark button visible)
  - When: the user clicks the "Mark as read" button (aria-label)
  - Then: the "New" badge and checkmark button disappear from that item; the item background returns to
    the neutral (non-highlighted) state; the item title changes from semibold to normal weight
  - Priority: P0 · Type: happy · Automated: yes (e2e: `mark-read removes badge and button`)

- **NOTIF-04** — **Mark-read persists across page navigation (dashboard cross-check)**
  - Given: a fresh user has just marked the welcome notification as read on `/notifications`
  - When: they navigate to the dashboard (`/`)
  - Then: the "Recent notifications" card shows the welcome notification WITHOUT a "New" badge and WITHOUT
    a "Mark as read" button, confirming the read state was persisted and the cache was invalidated
  - Priority: P0 · Type: happy · Automated: yes (e2e: `read state reflected on dashboard`)

- **NOTIF-05** — **Mark-read is idempotent (second click/request is a no-op)**
  - Given: the welcome notification has already been marked read
  - When: a POST to `/v1/notifications/{id}/read` is repeated (e.g. via network retry)
  - Then: the response is 200 OK (no error), the `ReadAt` timestamp does not change, and no duplicate
    row is created
  - Priority: P1 · Type: edge · Automated: yes (backend integration: mark-read idempotency; UI button hidden after first read)

- **NOTIF-06** — **Timestamp is displayed and formatted**
  - Given: the welcome notification is displayed
  - When: the user reads its timestamp
  - Then: a human-readable date/time string is shown (e.g. "Jun 19, 10:30 AM"); it is NOT an ISO
    string or epoch number
  - Priority: P1 · Type: happy · Automated: yes (e2e: `shows welcome notification unread`, asserts text pattern)

### Empty state

- **NOTIF-07** — **Empty state shown when user has no notifications**
  - Given: a fresh user whose Worker has NOT yet processed the welcome event (or templates are absent)
  - When: they navigate to `/notifications`
  - Then: the empty state renders with text "No notifications" and "You're all caught up — no notifications
    yet." and no feed list is shown
  - Priority: P1 · Type: edge · Automated: manual (requires a controlled environment where the Worker has
    not yet delivered; hard to reproduce deterministically in E2E without mocking)

- **NOTIF-08** — **Dashboard "Recent notifications" shows empty state when no notifications**
  - Given: a user with no in-app notifications
  - When: the dashboard loads
  - Then: the "Notifications" card shows "No notifications" / "You're all caught up." text; no list is shown
  - Priority: P1 · Type: edge · Automated: manual (same timing caveat as NOTIF-07)

### Pagination

- **NOTIF-09** — **Pagination controls absent when total <= 20**
  - Given: a fresh user with only the welcome notification (1 item, PAGE_SIZE = 20)
  - When: they are on `/notifications`
  - Then: no Previous / Next / page-number pagination controls are rendered
  - Priority: P1 · Type: edge · Automated: yes (e2e: `pagination absent for single-page feed`)

- **NOTIF-10** — **Pagination controls present when total > 20**
  - Given: a user with more than 20 notifications
  - When: they navigate to `/notifications`
  - Then: Previous, Next, and numbered page links are visible; Previous is disabled/grayed on page 1
  - Priority: P1 · Type: happy · Automated: manual (requires seeding > 20 notifications; not feasible
    via registration UI alone in a short E2E run)

- **NOTIF-11** — **Next page navigation loads older notifications**
  - Given: a user with 21+ notifications and pagination controls visible
  - When: they click "Next"
  - Then: the page increments to 2 and a different set of (older) items is shown; Previous becomes active
  - Priority: P1 · Type: happy · Automated: manual (same seeding caveat as NOTIF-10)

- **NOTIF-12** — **Previous disabled on page 1, Next disabled on last page**
  - Given: a user on the first page of a multi-page feed
  - When: checking the Previous button
  - Then: `aria-disabled="true"` is set and `pointer-events-none opacity-50` applies (not clickable)
  - Priority: P1 · Type: edge · Automated: manual

### Security & authorization

- **NOTIF-13** — **Unauthenticated access redirects to /login**
  - Given: a visitor with no session cookie
  - When: they navigate directly to `/notifications`
  - Then: they are redirected to `/login` (not a 401 JSON error)
  - Priority: P0 · Type: security · Automated: yes (e2e: `unauthenticated redirect to login`) using `ANONYMOUS`

- **NOTIF-14** — **A user cannot mark another user's notification as read**
  - Given: user A has notification `{idA}` and user B is authenticated
  - When: user B calls `POST /v1/notifications/{idA}/read`
  - Then: the API returns 404 (NotFoundException `notification.not_found`) — the handler scopes by `UserId`
    from the token, so a foreign id simply produces "not found"
  - Priority: P0 · Type: security · Automated: yes (backend integration verifies foreign notification id returns `notification.not_found`)

- **NOTIF-15** — **Tokens are NOT exposed in JS storage**
  - Given: an authenticated user on `/notifications`
  - When: `localStorage`, `sessionStorage`, and `document.cookie` are inspected from JS
  - Then: no JWT or refresh-token string is found; only the readable `mp_csrf` cookie may be present; session
    cookie is httpOnly and not accessible from JS
  - Priority: P0 · Type: security · Automated: yes (e2e: `session tokens not in JS storage`)

- **NOTIF-16** — **Module entitlement guard: notifications link absent when module disabled**
  - Given: a tenant that does not have the `notifications` entitlement
  - When: authenticated user checks the sidebar navigation
  - Then: no "Notifications" nav link is rendered; direct navigation to `/notifications` returns 403 or empty
  - Priority: P1 · Type: security · Automated: manual (requires admin to disable entitlement; not self-serve)

### Error handling

- **NOTIF-17** — **Network error on feed load shows error boundary**
  - Given: the `/v1/notifications/me` API call fails (e.g. 500 or network timeout)
  - When: the user opens `/notifications`
  - Then: an error state or error boundary is shown instead of an infinite spinner; the page does not crash
  - Priority: P1 · Type: error · Automated: manual (requires network interception / API stubbing)

- **NOTIF-18** — **Network error on mark-read shows a toast error and button re-enables**
  - Given: the user clicks "Mark as read" and the `POST /notifications/{id}/read` call fails
  - When: the error response is received
  - Then: a sonner toast error message appears; the checkmark button becomes clickable again (not stuck
    in pending/disabled state)
  - Priority: P1 · Type: error · Automated: manual (requires network interception)

### Realtime

- **NOTIF-19** — **New notification delivered via SSE invalidates the feed without a manual reload**
  - Given: the user is on `/notifications` and the SSE connection shows "Live"
  - When: another process triggers a notification for the user (e.g. a credit purchase completion)
  - Then: the new item appears at the top of the feed without a manual page refresh (the realtime event
    invalidates the `notifications` query root)
  - Priority: P1 · Type: happy · Automated: manual (requires triggering a backend event in the same E2E run)

### Accessibility

- **NOTIF-20** — **"Mark as read" button has accessible name**
  - Given: an unread notification is displayed
  - When: a screen reader scans the page
  - Then: the checkmark button has `aria-label="Mark as read"` making its purpose clear
  - Priority: P1 · Type: a11y · Automated: yes (e2e: `mark-as-read button has accessible label`)

- **NOTIF-21** — **Unread dot is aria-hidden**
  - Given: an unread notification has a colored dot indicator
  - When: assistive technology scans the element
  - Then: `aria-hidden="true"` is set so the decorative dot is not announced
  - Priority: P1 · Type: a11y · Automated: yes (e2e: implied by DOM check in the mark-read test)

- **NOTIF-22** — **Feed list is a semantic `<ul>` with `<li>` items**
  - Given: notifications are displayed
  - When: the DOM structure is inspected
  - Then: items are wrapped in a `<ul>` element and each notification is an `<li>`, providing list
    semantics for screen readers
  - Priority: P1 · Type: a11y · Automated: yes (e2e: `shows welcome notification unread with badge, button and timestamp`)

- **NOTIF-23** — **Unread-only filter shows only unread notifications**
  - Given: a fresh user's welcome notification is unread
  - When: they click the `Unread` filter
  - Then: the `Unread` button has `aria-pressed="true"` and the unread welcome notification remains visible with its `New` badge
  - Priority: P1 · Type: happy/edge · Automated: yes (e2e: `unread filter shows unread welcome then empty state after mark-all`)

- **NOTIF-24** — **Mark all read clears unread filter state**
  - Given: the user is viewing the unread-only feed and has one unread notification
  - When: they click `Mark all read`
  - Then: the unread feed switches to the `No unread notifications` empty state and the welcome item disappears from the unread-only list
  - Priority: P1 · Type: happy/edge · Automated: yes (e2e: `unread filter shows unread welcome then empty state after mark-all`; backend integration verifies idempotent read-all)

- **NOTIF-25** — **Notification preferences are per-user and in-app is mandatory**
  - Given: a user opens notification channel preferences on the Account profile page
  - When: preferences load
  - Then: email and push are configurable, in-app is visible but disabled; backend rejects disabling in-app and persists per-user email/push choices
  - Priority: P1 · Type: edge/security · Automated: backend integration (`Notification_preferences_default_enabled_can_be_changed_and_reject_required_inapp_channel`); frontend page structure is covered by account-profile smoke
