# Identity password reset

Status: Implemented
Owner module: Identity
Last updated: 2026-06-28

## Goal

User who forgot their password can request a reset link and set a new password without being signed in.
The flow must not reveal whether an email exists in the system.

This is a platform/base feature. Product modules do not implement their own password reset.

## User flow

1. User opens `/forgot-password`.
2. User enters email.
3. Frontend calls `POST /identity/auth/forgot-password`.
4. API always returns the same accepted response for syntactically valid input.
5. If the email belongs to an active account, Identity creates a one-time reset token and publishes an email delivery message through the outbox.
6. User opens `/reset-password?token=...`.
7. User enters a new password.
8. Frontend calls `POST /identity/auth/reset-password`.
9. Identity validates the token, changes the password, consumes the token and revokes active refresh tokens.
10. User is sent to login and must authenticate with the new password.

## API contract

### POST `/identity/auth/forgot-password`

Anonymous. Uses the auth rate-limit policy.

Request:

```json
{
  "email": "user@example.com"
}
```

Response:

- `202 Accepted`
- `ApiResponse<ForgotPasswordResponse>`
- Body does not say whether the account exists.

```json
{
  "data": {
    "accepted": true
  }
}
```

Validation:

- `email` is required.
- `email` must be a valid email address.
- Validation can reject malformed email, but existing vs non-existing account must look identical once email is syntactically valid.

### POST `/identity/auth/reset-password`

Anonymous. Uses the auth rate-limit policy.

Request:

```json
{
  "token": "raw-token-from-email",
  "newPassword": "new strong password"
}
```

Response:

- `204 No Content` on success.

Validation:

- `token` is required.
- `newPassword` uses the same minimum/maximum length rules as registration/change password.

Errors:

- Unknown token, expired token, consumed token, erased user and deleted user all return the same safe error code: `auth.password_reset_invalid`.
- New password equal to the current password returns `user.password_unchanged`.

## Database

Identity owns a new `password_reset_tokens` table.

Fields:

- `id` Guid v7 primary key.
- `user_id` Guid, the Identity user.
- `token_hash` string, unique, SHA-256 hash of the raw reset token.
- `created_at` DateTimeOffset UTC.
- `expires_at` DateTimeOffset UTC.
- `consumed_at` nullable DateTimeOffset UTC.

Rules:

- The raw token is never stored.
- A token can be used only once.
- Reset tokens are not tenant/user-owned RLS rows because the anonymous reset endpoint must look up a token before authentication.
- Outstanding tokens for a user are consumed before issuing a new one, so only the newest reset link is usable.

## Email delivery

Identity publishes `EmailDeliveryRequested` from `ModularPlatform.Notifications.Contracts` through the Identity outbox.

Do not call the Notifications Core module.
Do not use `SendNotificationCommand` for this flow, because that command creates an in-app notification row and would store the reset link in the in-app feed.

Email content contains:

- subject: password reset request;
- body: reset URL with `token` query parameter;
- no account-existence signal for unknown emails.

Config:

- `Identity:PasswordReset:TokenLifetimeMinutes`, default `30`.
- `Identity:PasswordReset:ResetUrl`, default `http://localhost:3000/reset-password`.

## Security and edge cases

- EC-PR-001 Unknown email returns the same `202` as an existing account.
- EC-PR-002 Soft-deleted or GDPR-erased user returns the same `202` and no usable token is created.
- EC-PR-003 Raw reset token is only in the email body, never in the DB.
- EC-PR-004 Stored token hash is unique and is the lookup key.
- EC-PR-005 Expired token cannot reset the password.
- EC-PR-006 Consumed token cannot be reused.
- EC-PR-007 Successful reset revokes all active refresh tokens for the user.
- EC-PR-008 Successful reset consumes all other outstanding reset tokens for the user.
- EC-PR-009 Reset flow is rate-limited by the auth policy.
- EC-PR-010 Product modules do not implement parallel reset/password tables.

## Frontend

Routes:

- `/forgot-password`
- `/reset-password?token=...`

Frontend actions:

- `forgotPasswordAction(email)`
- `resetPasswordAction(token, newPassword)`

UX:

- Login page has a "Forgot password?" link.
- Forgot form always shows a neutral success message after accepted request.
- Reset form requires token from URL; missing token shows a safe invalid-link state.
- After successful reset, redirect to `/login?reason=password-reset`.
