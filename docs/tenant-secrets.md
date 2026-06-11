# Tenant secrets — `ISecretProtector` + `tenant_secrets`

> **Status (2026-06-11):** **BUILT.** The `ModularPlatform.Secrets` building-block ships the `ISecretProtector`
> port (`Abstractions`) + the default `LocalMasterKeySecretProtector`; the Billing module stores per-tenant
> gateway credentials in `tenant_secrets` through it. KMS-backed providers are a later swap behind the same port.
>
> This is the seam for **any** secret a tenant owns (payment-gateway keys today; device/agent credentials later).
> It is **not** the per-subject PII crypto-shred path (`[Encrypted]` / `[PersonalData]` / `IDataSubject`) — those
> are per-user DEKs that erasure shreds. Tenant secrets must **survive** a user's GDPR erasure.

---

## 1. The port

`ISecretProtector` — `src/building-blocks/ModularPlatform.Abstractions/Ports.cs`:

```csharp
Task<ProtectedSecret> ProtectAsync(Guid? tenantId, string purpose, string plaintext, CancellationToken ct = default);
Task<string>          RevealAsync (Guid? tenantId, string purpose, ProtectedSecret secret, CancellationToken ct = default);
// ProtectedSecret(int KeyVersion, byte[] Ciphertext, byte[]? WrappedDek = null)
```

- `tenantId` is **nullable**: `null` ⇒ a **platform** secret (AAD uses the literal `platform`). A real tenant id ⇒
  a tenant secret.
- `purpose` is a stable string (e.g. `stripe.api_key`, `gopay.client_secret`). It is **part of the AAD**, so the
  same `(tenantId, purpose)` pair must be supplied on reveal — a secret sealed for one purpose cannot be unsealed
  under another.
- `ProtectedSecret` is what you persist (key version + ciphertext bytes [+ a wrapped DEK for KMS providers]).
  **Never** persist or log the plaintext.

Registration: `AddPlatformSecrets(config)` — `src/building-blocks/ModularPlatform.Secrets/PlatformSecrets.cs`
(mirrors `AddPlatformStorage`: `Secrets:Provider = local | kms`, default `local`).

---

## 2. The default impl — `local`

`LocalMasterKeySecretProtector` — `src/building-blocks/ModularPlatform.Secrets/LocalMasterKeySecretProtector.cs`:

- **AES-256-GCM** under a **versioned application master key** (`Secrets:MasterKeys` keyed by version +
  `Secrets:ActiveKeyVersion`). No external dependency — the platform runs on a plain Postgres box.
- Blob layout `[12-byte nonce][16-byte tag][ciphertext]` — the same shape as the platform's crypto-shred
  primitive, so a KMS provider can replace this later with the **same** `ProtectedSecret` shape (it additionally
  populates `WrappedDek`).
- **AAD = `tenantId|purpose`** (`{tenantId:N}` or the literal `platform`). A blob swapped between rows fails the
  GCM authentication tag — ciphertext is bound to its `(tenant, purpose)` row context.
- **Fail-fast outside Development:** `SecretsOptionsValidator` refuses the well-known dev placeholder master key in
  non-Dev environments (mirrors `RlsBootstrapper` / `JwtOptionsValidator`). Set `Secrets:MasterKeys:{n}` from a
  real 32-byte base64 secret in prod. (The non-Dev test harness seeds `Secrets:MasterKeys:1`.)

KMS / Key Vault / Vault providers (envelope-wrapping the DEK) are the planned later swap behind this same port —
chosen before GA (CLAUDE.md §10 "KEK/KMS").

---

## 3. The storage entity — `tenant_secrets`

Canonical: `TenantSecret` in `src/modules/Billing/…/Entities/PaymentGatewayEntities.cs` (table `tenant_secrets`):

| Column | Note |
|---|---|
| `TenantId` (`Guid`) | **EXPLICIT** — see §4 |
| `Purpose` (`string`, max 64) | what the secret is (`stripe.api_key`, …) |
| `KeyVersion` (`int`) | which master/KMS key version sealed it (rotation) |
| `Ciphertext` (`bytea`) | the sealed blob — **never** plaintext |
| `WrappedDek` (`bytea?`) | KMS-wrapped DEK (null for the local provider) |
| `CreatedAt` (`DateTimeOffset`) | UTC via `IClock` |

UNIQUE `(TenantId, Purpose)` — one secret per `(tenant, purpose)`. Seal on write:
`ConfigureGatewayHandler.SealAsync` calls `ISecretProtector.ProtectAsync` and upserts the row; reveal on read:
`BillingPaymentConfigStore.RevealAsync` reads the row and calls `ISecretProtector.RevealAsync`.

---

## 4. `ITenantScoped`, **NOT** `IUserOwned` (and the SYSTEM-read reason it's explicit)

Two non-negotiable shape rules, for two distinct reasons:

1. **NOT `IUserOwned`.** A tenant secret is owned by the *tenant*, not a user. If it were per-user/`IUserOwned`, a
   user's **GDPR erasure** (which crypto-shreds that user's DEK and RLS-isolates their rows) would destroy the
   tenant's payment keys. Tenant secrets are **excluded from the GDPR erasure fan-out** and the per-subject DEK
   path entirely. Do **not** mark them `[Encrypted]`/`[PersonalData]` — that is the per-subject mechanism.

2. **EXPLICIT `TenantId` (not the ambient `ITenantScoped` filter) in the current Billing impl.** The config store
   that reveals these secrets runs in the **SYSTEM Worker** context (webhook processing for an *arbitrary* tenant),
   where the EF tenant query filter is bypassed. An automatic per-tenant filter would **hide** the row the Worker
   needs, so `BillingPaymentConfigStore` filters by the explicit `tenantId` with `IgnoreQueryFilters()`. (The
   program's design language calls the data "tenant-scoped, not user-owned"; the concrete Billing entity realizes
   "tenant-scoped" as an **explicit** `TenantId` column precisely so a SYSTEM resolve can read any tenant — the
   same reason `PaymentConfiguration` is explicit. See `payments-multitenant.md` §3.)

---

## 5. Bans (these will corrupt or leak secrets)

- **NO secret in audit.** The ciphertext columns are audit-exempt; never let a plaintext credential reach
  `{module}_audit_entries`. (`ExecuteUpdate`/`ExecuteDelete` already bypass the audit interceptor; for these rows
  that's acceptable — they carry no audited PII.)
- **NO secret in the outbox / integration events.** Carry only a **reference** — `(TenantId, Purpose)` — and
  resolve it at the consumer via `RevealAsync`. A plaintext (or even ciphertext) credential on a durable envelope
  would persist for the envelope's lifetime and cross process boundaries.
- **NO secret in logs.** Not the plaintext, not the ciphertext, not the master key.
- **NO `tenant_secrets` in the GDPR erasure fan-out** — they must survive user erasure (§4).
- **NO marking them `[Encrypted]`/`[PersonalData]`** — that routes them through the per-subject DEK, which erasure
  shreds (§4).

---

## 6. Rotation

Master-key rotation = add a new `Secrets:MasterKeys:{n+1}`, bump `Secrets:ActiveKeyVersion`; old versions stay so
existing rows still reveal. Re-sealing legacy rows is a Jobs sweep `WHERE KeyVersion < current` (mirror
`PiiEncryptionBackfill`) — the same pattern the platform already uses for PII backfill.

---

## 7. Reuse for the future device module (seam #3)

The per-tenant secret seam is explicitly reusable for **device/agent credentials** (e.g. a local-agent API key,
an MQTT broker secret per tenant): same `ISecretProtector` + a `tenant_secrets`-shaped store with explicit
`TenantId`, same bans. No new mechanism — copy this one.
