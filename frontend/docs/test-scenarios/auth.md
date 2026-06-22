# Authentication Test Scenarios

Covers all logged-out flows for `/register`, `/login`, logout, and unauthenticated-redirect
behaviour. The session is httpOnly (iron-session `mp_session` cookie); tokens never reach JS.
A JS-readable `mp_csrf` cookie is the CSRF double-submit token.

Error codes are sourced from the backend validators (`RegisterUserValidator`, `LoginValidator`,
`LoginHandler`) and the frontend error-map (`lib/errors/error-map.ts`).

---

## Scenarios

### Registration — happy path

- **AUTH-01** — **Register with fresh email lands on dashboard**
  - Given the user is not logged in and navigates to `/register`
  - When they fill a unique email, optional display name, a valid password (≥8 chars), accept the Terms checkbox, and submit
  - Then they are redirected to `/` (dashboard), the app shell is visible, and a success toast "Account created! Welcome." appears
  - Priority: P0 · Type: happy · Automated: yes (e2e: `register › happy path: fresh email lands on dashboard`)

- **AUTH-02** — **Already-authenticated user is redirected away from `/register`**
  - Given the user has a valid session
  - When they navigate to `/register`
  - Then the auth layout redirects them to `/` immediately (no flicker of the form)
  - Priority: P1 · Type: edge · Automated: manual (requires authenticated state + navigation to /register — not tested in ANONYMOUS suite)

---

### Registration — validation (client-side, blocked before submit)

- **AUTH-03** — **Empty form submission is blocked**
  - Given the user is on `/register` and submits without filling any field
  - When they click "Create account"
  - Then inline validation errors appear: "Email is required", "Password must be at least 8 characters", and a terms error — the form does NOT navigate away
  - Priority: P1 · Type: error · Automated: yes (e2e: `register › validation: empty submit shows inline errors`)

- **AUTH-04** — **Invalid email format is rejected**
  - Given the user fills `not-an-email` in the email field
  - When they submit
  - Then the email field shows "Enter a valid email" and submission is blocked
  - Priority: P1 · Type: error · Automated: yes (e2e: `register › validation: invalid email format`)

- **AUTH-05** — **Short password (< 8 chars) is rejected**
  - Given the user fills a valid email and password `abc1234` (7 chars)
  - When they submit
  - Then the password field shows "Password must be at least 8 characters"
  - Priority: P1 · Type: error · Automated: yes (e2e: `register › validation: short password`)

- **AUTH-06** — **Terms checkbox not accepted blocks submission**
  - Given the user fills valid email and password but leaves the Terms checkbox unchecked
  - When they submit
  - Then an error "You must accept the Terms and Privacy Policy" appears below the checkbox and the form does not submit
  - Priority: P1 · Type: error · Automated: yes (e2e: `register › validation: terms not accepted blocks submit`)

---

### Registration — server-side errors

- **AUTH-07** — **Duplicate email shows field-level error**
  - Given a user with email `alice@test.local` already exists
  - When a second registration attempt uses the same email (correct password, Terms accepted)
  - Then the server returns `user.email_taken` (409 Conflict) and the email field shows "That email is already registered." — no navigation occurs
  - Priority: P0 · Type: error · Automated: yes (e2e: `register › duplicate email shows field error`)

---

### Login — happy path

- **AUTH-08** — **Login with correct credentials lands on dashboard**
  - Given a registered user with known email/password exists
  - When they fill correct credentials and click "Sign in"
  - Then they land on `/` with the app shell visible and a "Welcome back!" toast
  - Priority: P0 · Type: happy · Automated: yes (e2e: `login › happy path: correct credentials land on dashboard`)

- **AUTH-09** — **Already-authenticated user is redirected away from `/login`**
  - Given the user has a valid session
  - When they navigate to `/login`
  - Then the auth layout redirects them to `/` (no form shown)
  - Priority: P1 · Type: edge · Automated: manual (requires authenticated context)

---

### Login — error cases

- **AUTH-10** — **Wrong password shows generic error (no user enumeration)**
  - Given a registered user exists with email `alice@test.local`
  - When they submit the login form with a wrong password
  - Then the password field shows "Incorrect email or password." (error code `auth.invalid_credentials`) and the user stays on `/login`
  - Then the same error message is shown for a non-existent email (timing-equalized; no oracle)
  - Priority: P0 · Type: error · Automated: yes (e2e: `login › wrong password shows generic error`)

- **AUTH-11** — **Empty email or password is blocked client-side**
  - Given the user submits the login form without filling any field
  - Then "Email is required" and "Password is required" appear; no network request is made
  - Priority: P1 · Type: error · Automated: yes (e2e: `login › empty submit shows inline errors`)

- **AUTH-12** — **Invalid email format is rejected client-side**
  - Given the user types `notanemail` in the email field and submits
  - Then "Enter a valid email" appears; no network request is made
  - Priority: P1 · Type: error · Automated: yes (e2e: `login › invalid email format blocked client-side`)

---

### Logout

- **AUTH-13** — **Logout via user menu returns to `/login`**
  - Given the user is authenticated and on the dashboard
  - When they open the user menu (sidebar footer) and click "Sign out"
  - Then a "Signed out." toast appears, they are redirected to `/login`, and the session cookie is cleared
  - Priority: P0 · Type: happy · Automated: yes (e2e: `logout › user menu sign-out redirects to login`)

- **AUTH-14** — **After logout, navigating to a protected route redirects to `/login`**
  - Given the user just logged out
  - When they navigate to `/` (dashboard)
  - Then they are redirected to `/login` (no protected content shown)
  - Priority: P0 · Type: security · Automated: yes (e2e: `logout › protected route redirects after logout`)

---

### Unauthenticated redirect

- **AUTH-15** — **Unauthenticated access to `/` redirects to `/login`**
  - Given the user has no session
  - When they navigate to `/`
  - Then they land on `/login` (server-side redirect from the tenant layout)
  - Priority: P0 · Type: security · Automated: yes (e2e: `unauthenticated › accessing protected route redirects to /login`)

- **AUTH-16** — **Unauthenticated access to `/billing` redirects to `/login`**
  - Given the user has no session
  - When they navigate to `/billing`
  - Then they land on `/login`
  - Priority: P1 · Type: security · Automated: yes (e2e: `unauthenticated › accessing /billing redirects to /login`)

- **AUTH-17** — **Intent is NOT preserved in URL (no `?next=` leak)**
  - Given the user navigates to `/billing` without a session
  - Then the redirect URL is exactly `/login` with no query parameters — no path appears in the URL
  - Priority: P1 · Type: security · Automated: yes (e2e: `unauthenticated › redirect URL contains no leaking query params`)

---

### Token / session security

- **AUTH-18** — **Tokens never appear in JS storage or readable cookies**
  - Given the user is authenticated
  - Then `localStorage`, `sessionStorage` contain no access or refresh token strings
  - Then `document.cookie` does NOT expose `mp_session` (httpOnly) — only `mp_csrf` is readable
  - Priority: P0 · Type: security · Automated: yes (e2e: `session security › tokens are httpOnly and not in JS storage`)

- **AUTH-19** — **Password does not appear in the URL after login or register**
  - Given the user fills and submits the login or register form
  - Then the resulting URL (`page.url()`) does not contain the password string
  - Priority: P0 · Type: security · Automated: yes (e2e: `session security › password never in URL after login`)

---

### Accessibility

- **AUTH-20** — **Login form fields have correct labels and ARIA**
  - Given the user visits `/login`
  - Then Email and Password inputs have visible `<label>` elements; error states set `aria-invalid=true` and link to an error message via `aria-describedby`
  - Priority: P2 · Type: a11y · Automated: manual (visual + AT inspection)

- **AUTH-21** — **Register Terms checkbox is keyboard-operable**
  - Given the user tabs to the Terms checkbox
  - Then pressing Space toggles `aria-checked` between true/false
  - Priority: P2 · Type: a11y · Automated: manual

---

### Edge / rate-limit (manual — not safe to automate against shared backend)

- **AUTH-22** — **Account lockout after 5 failed login attempts**
  - Given the user submits 5 wrong passwords for a real account
  - Then the 6th attempt with the CORRECT password returns `auth.locked_out` ("Account temporarily locked. Try again later.") while a wrong password still returns `auth.invalid_credentials` (no enumeration)
  - Priority: P1 · Type: edge/security · Automated: manual (would exhaust shared backend lockout state)

- **AUTH-23** — **Rate-limit (429) on `/login` shows user-friendly message**
  - Given the rate limit on the `auth` policy is exceeded from the test IP
  - Then the error "Too many requests. Please slow down." is displayed
  - Priority: P1 · Type: error · Automated: manual (shared rate-limit bucket; would poison other tests)
