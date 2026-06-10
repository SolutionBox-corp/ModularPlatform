# PII-at-rest column encryption + blind index — design

> Completes the crypto-shred vision (plan §8): live PII columns encrypted under the per-subject DEK,
> lookups via a keyed HMAC blind index, plus the audit-PII follow-ups (AES-GCM AAD binding, PasswordHash
> blanking on erasure). User decision 2026-06-10: full scope now.

## Threat model / goal

A DB dump (or read-replica leak) must not expose user PII in plaintext, and GDPR erasure (shredding the
subject's DEK) must render the encrypted values permanently unreadable — live columns AND audit trail.

## Mechanics (the EF-compatible split)

EF `ValueConverter`s are stateless — they cannot see the row's subject at WRITE time, but the stored
envelope is self-describing (`penc:v2:{subjectId}:{blob}`) at READ time. Hence:

- **Write path = `PersonalDataEncryptionInterceptor`** (new, Persistence building-block, registered AFTER
  `AuditInterceptor` in `AddModuleDbContext` so audit still sees model-side plaintext): on SavingChanges,
  for every `[Encrypted]` string property of an `IDataSubject` entity, replaces the current value with
  `IPersonalDataProtector.Protect(subjectId, value)` (skips values already protected). After SavedChanges it
  restores the in-memory plaintext so the tracked instance stays usable.
- **Read path = decrypting `ValueConverter`** applied by a `PlatformDbContext` model convention to every
  `[Encrypted]` property: `FromProvider`: `IsProtected(v) ? TryReveal(v) : v` (pass-through for legacy
  plaintext + tombstones); `ToProvider`: identity (encryption is the interceptor's job). Works on BOTH the
  write context and the interceptor-free `ReadDbContextFactory` contexts (converters are model-level).
- **`[Encrypted]`** attribute lives in Abstractions next to `[PersonalData]`; an `[Encrypted]` property MUST
  also be `[PersonalData]` and its entity `IDataSubject` (new ArchUnitNET rule). `ExecuteUpdate`/`ExecuteDelete`
  bypass the interceptor (documented §4 caveat) — constants written that way are stored as-is (used
  deliberately by the eraser tombstones).

## Blind index (login & uniqueness without plaintext)

- `users.NormalizedEmail` (plaintext lookup column) is REPLACED by `EmailHash` =
  Base64(HMAC-SHA256(BlindIndexKey, `email.Trim().ToUpperInvariant()`)) with the UNIQUE index moved to it.
- `IBlindIndexHasher` port (Abstractions), impl in Gdpr (`HmacBlindIndexHasher`), key from
  `Gdpr:Encryption:BlindIndexKey` — **platform-wide secret** (login is pre-auth/cross-subject, so a
  per-subject key is impossible), fail-fast `IValidateOptions` outside Development (JwtOptions pattern).
- All four lookup sites switch to `EmailHash == hasher.Hash(normalized)`:
  `LoginHandler`, `RegisterUserHandler` (+ its `user.email_taken` conflict), `IdentitySeeder` (batch IN over
  hashes), `RefreshTokenHandler` family revoke path if it queries by email (verify).
- Erasure tombstone: eraser sets `Email = "erased-{id:N}@erased.invalid"` (plaintext constant — non-PII,
  non-routable) and `EmailHash = hash(tombstone)` → uniqueness preserved deterministically.

## Envelope v2 + AAD

- `CryptoShredder.Encrypt/Decrypt` gain an optional `aad` parameter; `PersonalDataProtector` emits
  `penc:v2:` envelopes sealed with `subjectId.ToByteArray()` as AES-GCM associated data (an envelope
  re-attached to another subject fails authentication). `TryReveal` keeps reading `penc:v1:` (no AAD) —
  existing audit envelopes stay readable until natural erasure.

## Identity changes

- `User`: `Email` + `DisplayName` become `[Encrypted]` (stay `[PersonalData]`); `NormalizedEmail` is DROPPED;
  `EmailHash` added (max 64, UNIQUE, NOT `[PersonalData]` — an HMAC is not reversible).
- Migration `IdentityPiiEncryption`: add `EmailHash` (empty default), drop the `NormalizedEmail` index +
  column. **Backfill = `PiiEncryptionBackfill` hosted service** (idempotent: rows with `EmailHash = ''` get
  hash + encrypted values computed in C#), runs after migrations on every host; fresh DBs no-op.
- `IdentityPersonalDataEraser` additionally blanks `PasswordHash` (credential hygiene — login on an erased
  account must fail on credentials, not only on the non-routable email).
- JWT `email` claim, integration-event payloads and Notification rows still carry plaintext email by design
  (scoped out — flagged in the doc; the welcome path needs the address to actually send mail).

## Test impact

`AuditPiiEncryptionTests`, `GdprIntegrationTests`, `NotificationsIntegrationTests` (raw `NormalizedEmail`
SQL helper) updated; new tests: login/register via blind index round-trip, DB column stores `penc:v2:`,
erased user → ciphertext unreadable + PasswordHash blank + login fails, AAD cross-subject rejection (unit),
ArchUnit `[Encrypted] ⇒ [PersonalData] + IDataSubject`.
