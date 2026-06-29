# Identity email verification

Status: Implemented
Owner module: Identity
Last updated: 2026-06-28

## Goal

Registered users must prove that they own their email address.
The platform can still allow login before verification, but the account/profile UI must clearly show that the email is unverified and provide a resend action.

Product modules do not implement their own email verification.

## User flow

1. User registers.
2. Identity creates the user with `EmailConfirmed = false`.
3. Identity creates a one-time email verification token and sends a verification email through the outbox.
4. User opens `/verify-email?token=...`.
5. Frontend calls `POST /identity/auth/verify-email`.
6. Identity validates the token, marks the user email as confirmed and consumes outstanding verification tokens.
7. User profile now returns `emailConfirmed = true`.

## API contract

### POST `/identity/auth/verify-email`

Anonymous. Uses the auth rate-limit policy.

Request:

```json
{
  "token": "raw-token-from-email"
}
```

Response:

- `204 No Content` on success.

Errors:

- Unknown token, expired token, consumed token, erased user and deleted user return `auth.email_verification_invalid`.
- Reusing a token after the email is already verified also returns the same safe invalid-token error.

### POST `/identity/users/me/email-verification`

Authenticated human user only. Uses the auth rate-limit policy.

Request: no body.

Response:

- `202 Accepted`
- `ApiResponse<RequestEmailVerificationResponse>`

```json
{
  "data": {
    "accepted": true,
    "alreadyVerified": false
  }
}
```

Rules:

- If the caller is already verified, return accepted with `alreadyVerified = true` and do not send email.
- If the caller is unverified, consume older outstanding verification tokens, create a new one and send an email.

### GET `/identity/users/me`

`UserProfileResponse` adds:

```json
{
  "emailConfirmed": false
}
```

## Database

Identity adds fields to `users`:

- `EmailConfirmed` bool, default `false`.
- `EmailConfirmedAt` nullable DateTimeOffset UTC.

Identity owns a new `email_verification_tokens` table:

- `id` Guid v7 primary key.
- `user_id` Guid.
- `token_hash` string, unique, SHA-256 hash of the raw token.
- `created_at` DateTimeOffset UTC.
- `expires_at` DateTimeOffset UTC.
- `consumed_at` nullable DateTimeOffset UTC.

Rules:

- The raw token is never stored.
- A token can be used only once.
- Tokens are not RLS-owned because anonymous verification must look up the token before authentication.
- Creating a new verification token consumes older outstanding tokens for that user.

## Email delivery

Identity publishes `EmailDeliveryRequested` from `ModularPlatform.Notifications.Contracts` through the Identity outbox.

Do not call Notifications Core.
Do not use `SendNotificationCommand`, because verification links should not be stored in the in-app feed.

Config:

- `Identity:EmailVerification:TokenLifetimeMinutes`, default `1440`.
- `Identity:EmailVerification:VerifyUrl`, default `http://localhost:3000/verify-email`.

## Frontend

Routes:

- `/verify-email?token=...`

Profile/account:

- Read `emailConfirmed` from `accountQueries.profile()`.
- If false, show an email verification card/banner with a resend button.
- Resend calls `POST /identity/users/me/email-verification`.
- After successful verification, the verify page links back to login/account and invalidates/refetches profile when the user is already signed in.

## Edge cases

- EC-EV-001 New users start with `emailConfirmed = false`.
- EC-EV-002 Register sends a verification email but does not use in-app notifications.
- EC-EV-003 Raw token exists only in the email body, never in DB.
- EC-EV-004 Expired/consumed/unknown token returns the same `auth.email_verification_invalid`.
- EC-EV-005 Resend for already verified user is a no-op accepted response.
- EC-EV-006 Resend consumes previous outstanding verification tokens.
- EC-EV-007 Verify is idempotent at the user state level: once verified, user remains verified and `EmailConfirmedAt` is not restamped.
- EC-EV-008 Soft-deleted or GDPR-erased user cannot verify.
- EC-EV-009 Product modules do not use `EmailConfirmed` as their own table; they read Identity profile/session state or their own projection if explicitly needed.
