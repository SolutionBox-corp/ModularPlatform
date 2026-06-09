# Audit-PII Encryption-at-Rest (crypto-shred) — Design

**Date:** 2026-06-09 · **Status:** approved (proceeding to implementation per `/goal`)

## Problem

`AuditInterceptor` (building-block `ModularPlatform.Persistence`) writes one immutable `AuditEntry` per
Create/Update/Delete into the per-module `{module}_audit_entries` table, serializing changed column values into the
`NewValues` JSONB. Today PII (e.g. `User.Email`, `User.DisplayName`, `Notification.Title/Body`) lands there as
**plaintext**. GDPR erasure (`CryptoShredder` + the `SubjectKey` DEK envelope) cannot reach it, so "right to be
forgotten" leaves recoverable PII in the audit trail (and its backups). This is a compliance hole, not a feature —
the `AuditEntry` docstring already *describes the intended end-state* ("PII new-values are the already
crypto-shredded ciphertext"); this work makes that true.

## Decisions (settled with the user)

1. **Crypto-shred** (not hash/omit): audit PII is stored **encrypted under the data subject's DEK**, so it stays
   forensically recoverable until erasure, then becomes permanently unrecoverable when the DEK is shredded.
2. **Lazy get-or-create DEK in a platform port** (not eager-on-registration, not synchronous-in-registration which
   would break the module boundary). The first audit row — the user-create itself — is encrypted.
3. **Include a decrypt read path** (admin-only), so the forensic value is usable until erasure. Canonical example
   in Identity; other modules follow the same pattern when needed.

## Architecture

### PII marking
`[PersonalData]` attribute in `ModularPlatform.Abstractions`, placed on **string** entity properties:
`User.Email`, `User.DisplayName`, `Notification.Title`, `Notification.Body`. Only marked fields are encrypted;
everything else is audited in clear. The interceptor reads the attribute via `IProperty.PropertyInfo`.

### Subject resolution
Marker `IDataSubject { Guid SubjectId { get; } }` in Abstractions. The audited entity declares whose PII it holds —
`User` returns `Id`, `Notification` (and other `IUserOwned`) returns `UserId` — via **explicit** interface
implementation so EF does not map `SubjectId` as a column. The interceptor needs no entity-type knowledge. If a
`[PersonalData]` field is found on an entity that is not `IDataSubject`, the value is **redacted** (fail-safe), and
an ArchUnitNET rule forbids that combination so it can't ship.

### Platform port (boundary-clean)
```csharp
public interface IPersonalDataProtector
{
    string Protect(Guid subjectId, string plaintext);     // -> self-describing envelope, get-or-creates the DEK
    bool TryReveal(string value, out string plaintext);    // decrypts a protected envelope iff the DEK is still live
}
```
- `Persistence` (interceptor) depends only on this Abstractions port. **Gdpr** implements it (it owns `SubjectKey` +
  `CryptoShredder`) and registers it in `GdprModule.RegisterServices`. No building-block → module-Core reference.
- Envelope format is the protector's internal detail: a sentinel-prefixed string
  `penc:v1:{base64(subjectId)}:{base64(nonce|tag|ciphertext)}` (AES-256-GCM via `CryptoShredder`). `TryReveal`
  recognizes the prefix, parses the subject, and decrypts — so the read path needs no out-of-band "which columns are
  PII" list. A value without the prefix → `TryReveal` returns false (left as-is).
- **No `AuditEntry` schema change, no migrations** — the envelope rides inside the existing `NewValues` JSON value.

### Protector data access (reentrancy + RLS handled)
The protector is called **from inside** the `AuditInterceptor`, so it MUST use a **separate** `GdprDbContext`
instance with **no `AuditInterceptor`** (else infinite reentrancy) on its **own connection**. `subject_keys` is
`IUserOwned` (RLS-protected), and the protector manages keys for *arbitrary* subjects, so it runs as **system**:
the context is built with the runtime data connection (`RlsConnectionString.ForRuntime(read, rls)`) +
`PrincipalSessionConnectionInterceptor(new SystemTenantContext())`, which stamps `app.is_system=on` → RLS bypassed.
`AuditInterceptor` is a **singleton**, so the protector is registered **singleton** too; it builds these short-lived
contexts on demand. It reads the subject's DEK **live from the DB on every call** (no in-process DEK cache): a
shredded key is honoured immediately (cross-process — the Worker's shred is seen by the Api at once) and key bytes are
never retained in memory. (An adversarial security review rejected an earlier write-path cache for exactly this:
a cached DEK would survive erasure in the singleton's heap and re-encrypt post-erasure PII under a destroyed key.)
- `Protect` get-or-creates the `SubjectKey` (`GenerateDek`, insert with `UNIQUE UserId`, catch `DbUpdateException`
  → reload) — idempotent; the separate transaction commits independently (an orphan DEK after a main-save rollback
  is harmless). `Protect` may serve the DEK from cache.
- `TryReveal` **always re-reads** the `SubjectKey` from the DB and refuses (returns false) when `WrappedDek is null`
  / `DeletedAt` is set — so a shredded key can't be decrypted even if a DEK is still cached.

### Interceptor change
`AuditInterceptor` gains an optional `IPersonalDataProtector? protector = null`. When building the per-column value
map, for each `[PersonalData]` property it resolves the entity's subject (`IDataSubject`) and replaces the value
with `protector.Protect(subjectId, value)`. If the protector is absent (Gdpr disabled) or the subject can't be
resolved, it stores a redaction placeholder (`"penc:redacted"`) — **never plaintext**. AES is synchronous, so this
works in both the sync and async save paths. `ChangedColumns` (column *names*, not PII) is unchanged.

### Decrypt read path (canonical: Identity)
`GetUserAuditTrail` query slice in Identity: admin reads `identity_audit_entries` for a user, and each `NewValues`
value is passed through `IPersonalDataProtector.TryReveal` → decrypted PII, or left/shown as `"[erased]"` when the
key is shredded. Endpoint gated `.RequirePermission(PlatformPermissions.AuditRead)` (new permission const, auto-seeded
to the system `admin` role). Other modules replicate this slice when they need a viewer.

### Erasure reach (already wired)
The existing `ShredSubjectKey` step of the erasure fan-out nulls `WrappedDek` + stamps `DeletedAt`. After that every
audit envelope under that DEK is unrecoverable and `TryReveal` returns false. **No change to the erasure flow** — this
design makes the audit trail finally honor it.

## Testing
- **Unit (Gdpr):** `Protect`→`TryReveal` round-trip; `TryReveal` after shred returns false; get-or-create idempotent.
- **Integration (Identity):** register + update a user → `identity_audit_entries.NewValues` for Email/DisplayName are
  `penc:v1:` envelopes and the **raw row contains no plaintext email substring**; the admin audit-trail query reveals
  them; after erasure the trail shows `"[erased]"` and the raw row still holds no plaintext.
- **Integration (Notifications):** an in-app notification's Title/Body audit value is enveloped.
- **Arch:** every entity with a `[PersonalData]` property implements `IDataSubject`.

## Risks / edges (handled)
- Singleton interceptor + separate-connection protector → reentrancy-safe (different context instance, no audit
  interceptor on it). **Verified by `Subject_key_creation_is_never_audited...`** — the DEK never reaches the audit trail.
- No DEK cache: encrypt and decrypt both read the live key and refuse a shredded one → erasure honoured immediately.
- Protector missing (Gdpr disabled) **or `subjectId == Guid.Empty`** → fail-safe redaction, never plaintext.
- First user-create audit row is encrypted (lazy get-or-create); a transient DB error on create is rethrown (not
  silently redacted).
- A corrupted/format-drifted envelope degrades to "not revealable" (`TryReveal` catches `FormatException`/
  `ArgumentException`/`OverflowException`) instead of 500-ing the admin read.
- `ExecuteUpdate`/`ExecuteDelete` already bypass the interceptor (e.g. erasure's notification blanking) — so they
  write no audit rows and need no encryption; consistent with the existing audit caveat.

## Out of scope (follow-ups, from the security review — none are confidentiality leaks)
- Bind `subjectId` (and ideally entity/column) as **AES-GCM AAD** so envelope-relocation is a detectable failure,
  not merely a positional convention (today a wrong subjectId → tag mismatch → not-revealable, which is safe).
- Blank `PasswordHash` on erasure for credential hygiene (it's a non-reversible hash, not direct PII).
- Decrypt viewers for modules other than Identity (same pattern, build on demand).
- KEK-wrapping of the DEK in a KMS (the `SubjectKey`/`CryptoShredder` docstrings already mark this out of scope; dev
  stores the raw DEK in `WrappedDek`).
- Per-subject random-nonce birthday bound is fine for expected audit volume; revisit (counter nonce / DEK rotation)
  only for pathological high-write subjects.
