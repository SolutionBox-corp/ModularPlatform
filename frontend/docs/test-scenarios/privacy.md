# Privacy / GDPR — Test Scenario Catalog

Page: `/account/privacy`  
Backend module: `src/modules/Gdpr`  
Auth requirement: authenticated user (all scenarios); destructive scenarios use a throwaway fresh user.

The page has three sections: **Consent preferences** (three Switch toggles), **Export your data** (synchronous download), and **Delete account** (Dialog with typed confirmation gate).

> Note on export flow: the `GET /v1/gdpr/me/export` endpoint is **synchronous** — it returns a JSON document in-band (no 202/operation poll). The UI triggers a browser download on success. There is no `OperationStatus` polling for this flow in the current implementation.

---

## Consent Toggles

| ID | Title | Given / When / Then | Priority | Type | Automated |
|---|---|---|---|---|---|
| PRIV-01 | Page renders with consent section visible | Given an authenticated user navigates to `/account/privacy` | When the page loads | Then the heading "Consent preferences" and all three switch toggles (Marketing emails, Analytics & improvement, Third-party sharing) are visible | P0 | happy | yes (e2e: "Privacy page renders consent toggles") |
| PRIV-02 | Grant a consent — switch flips and persists across reload | Given a fresh user on `/account/privacy` with "Marketing emails" switch unchecked | When the user clicks the switch | Then `aria-checked` becomes `"true"`, a "Consent granted." toast appears, and after a page reload the switch remains checked | P0 | happy | yes (e2e: "toggle marketing consent — grant persists after reload") |
| PRIV-03 | Withdraw a consent — switch flips and persists | Given a fresh user who has already granted "Analytics & improvement" | When the user clicks the switch to turn it off | Then `aria-checked` becomes `"false"`, a "Consent withdrawn." toast appears, and after reload the switch is still unchecked | P0 | happy | yes (e2e: "toggle analytics consent — withdraw persists after reload") |
| PRIV-04 | Toggle same consent type multiple times | Given a fresh user | When the user toggles "Third-party sharing" on, then immediately off | Then the switch settles at `aria-checked="false"` and the final backend state reflects withdrawn | P1 | edge | yes (e2e: "toggle third-party sharing twice settles at withdrawn") |
| PRIV-05 | Switch is disabled while its mutation is in-flight | Given the user clicks a switch that has not yet resolved | When the response is pending | Then the targeted switch is `disabled` (no double-submit) | P1 | edge | manual (requires network throttling) |
| PRIV-06 | Consent state derives from newest history entry | Given a user who toggled a type multiple times (append-only history) | When the page is reloaded | Then the displayed checked state reflects the newest record only | P1 | edge | yes (implicitly covered by PRIV-02 and PRIV-03 reload assertions) |
| PRIV-07 | Error toast on consent API failure | Given a network error or 4xx from `/gdpr/consents/grant` | When the user toggles a switch | Then an error toast is shown and the switch does not change state persistently | P1 | error | manual (requires network intercept) |
| PRIV-08 | Unauthenticated access redirects | Given an unauthenticated user | When they navigate to `/account/privacy` | Then they are redirected to `/login` | P0 | security | yes (e2e: "unauthenticated access to privacy page redirects to login") |
| PRIV-09 | Consent types are keyboard navigable | Given the user focuses the first switch via Tab | When they press Space | Then the switch toggles and a toast appears (keyboard parity with mouse) | P1 | a11y | manual |
| PRIV-10 | Skeleton placeholders shown while loading | Given a slow network | When the page first loads before the consent query resolves | Then Skeleton placeholders appear in the consent list area | P2 | edge | manual (requires network throttling) |

---

## Export Data

| ID | Title | Given / When / Then | Priority | Type | Automated |
|---|---|---|---|---|---|
| PRIV-11 | Export button visible | Given an authenticated user on `/account/privacy` | When the page loads | Then a button labelled "Download my data" is visible in the Export section | P0 | happy | yes (e2e: "Privacy page renders export section") |
| PRIV-12 | Clicking export triggers download and shows downloaded state | Given the user clicks "Download my data" | When the backend returns successfully | Then the button temporarily shows "Preparing export…" (spinner), a "Your data export is ready and has been downloaded." toast appears, and the "Downloaded" confirmation mark is shown | P0 | happy | yes (e2e: "export — button transitions to downloaded state") |
| PRIV-13 | Export button disabled while request is in-flight | Given the user has clicked "Download my data" and the request is pending | When they try to click again | Then the button is `disabled`, preventing duplicate downloads | P1 | edge | manual (requires network throttle) |
| PRIV-14 | Export API error shows error toast | Given `/gdpr/me/export` returns a 5xx | When the user clicks "Download my data" | Then an error toast is shown and the "Downloaded" state is NOT set | P1 | error | manual (requires network intercept) |
| PRIV-15 | Export endpoint requires authentication | Given an unauthenticated request to `GET /v1/gdpr/me/export` | Then a 401 is returned (the UI redirects to login before this is reached) | P0 | security | yes (covered by PRIV-08) |

---

## Delete Account (Erase)

| ID | Title | Given / When / Then | Priority | Type | Automated |
|---|---|---|---|---|---|
| PRIV-16 | Delete account button is visible and opens dialog | Given an authenticated user on `/account/privacy` | When the user clicks "Delete my account" | Then a Dialog with title "Delete account permanently" opens | P0 | happy | yes (e2e: "erase dialog opens on trigger click") |
| PRIV-17 | Delete button in dialog is disabled until phrase is typed | Given the dialog is open | When the confirmation input is empty or has wrong text | Then the "Permanently delete my account" button is `disabled` | P0 | happy | yes (e2e: "erase confirm button disabled before correct phrase") |
| PRIV-18 | Partial phrase does not enable the button | Given the dialog is open and the user types "delete" (partial) | Then the submit button remains `disabled` | P0 | edge | yes (e2e: "erase confirm button disabled for partial phrase") |
| PRIV-19 | Cancel dismisses dialog and resets input | Given the dialog is open and the user has typed the confirmation phrase | When the user presses Escape or closes the dialog without submitting | Then the dialog closes, the confirmation input is cleared, and no erasure is requested | P0 | happy | yes (e2e: "erase dialog cancel clears input and closes") |
| PRIV-20 | Full flow: type phrase, confirm, redirect to /login | Given a throwaway fresh user, the dialog is open, and they type "delete my account" | When they click "Permanently delete my account" | Then the button shows "Erasing…", a success toast appears, the dialog closes, and the user is redirected to `/login?reason=erased` | P0 | happy | yes (e2e: "erase account full flow redirects to login") |
| PRIV-21 | Submit button only enabled for exact phrase (case-insensitive, trimmed) | Given the user types "  Delete My Account  " (with spaces and uppercase) | Then the button is enabled (the UI does `.trim().toLowerCase()`) | P1 | edge | yes (e2e: "erase confirm button enabled for phrase with leading/trailing spaces and uppercase") |
| PRIV-22 | Uppercase with spaces enables the button (not disabled) | Given the user types "DELETE MY ACCOUNT" (uppercase, no extra whitespace) | Then the button is **enabled** — the UI applies `.trim().toLowerCase()` before comparing, so "DELETE MY ACCOUNT" normalises to "delete my account" which matches the required phrase | P1 | edge | yes (e2e: "erase confirm button enabled for phrase with leading/trailing spaces and uppercase") |
| PRIV-23 | Erasure API error shows error toast | Given the erasure API returns a 5xx | When the user confirms | Then an error toast is shown and no redirect occurs | P1 | error | manual (requires network intercept) |
| PRIV-24 | Erase endpoint rejects unauthenticated request | Given an unauthenticated POST to `/gdpr/me/erase` | Then a 401 is returned | P0 | security | yes (covered by PRIV-08) |
| PRIV-25 | Erase dialog has descriptive content warning | Given the dialog is open | Then it contains a summary of what will be erased ("profile, consents, notification history, billing records") and states this cannot be undone | P1 | a11y | yes (e2e: "erase dialog shows irreversibility warning") |
| PRIV-26 | Input autocomplete is disabled | Given the dialog is open | Then the confirmation input has `autocomplete="off"` to prevent browser autofill | P2 | security | yes (e2e: "erase confirm input has autocomplete=off") |

---

## Summary

- 26 scenarios total
- 18 automated (E2E), 8 manual (require network throttling/intercept or precise timing)
- Coverage: consent grant/withdraw persistence, unauthenticated redirect, export download flow, erase dialog gate, confirm phrase validation, post-erase redirect
