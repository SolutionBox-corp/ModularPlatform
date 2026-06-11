---
name: writing-modularplatform-tests
description: Write tests for a ModularPlatform feature or module — integration tests on the shared Testcontainers-Postgres host harness plus ArchUnitNET boundary rules. Use right after implementing a feature or module. Enforces reuse of the shared harness.
---

# Writing ModularPlatform tests

**Read `CLAUDE.md` §9 first.** Test behavior through the REAL seams (HTTP endpoints / dispatcher / DbContext /
outbox), never the internals. **REUSE the shared harness — do not write a new Testcontainers fixture.**

## Reuse the shared harness (DRY)
`tests/ModularPlatform.IntegrationTesting` → **`PlatformApiFactory`** already boots the full Api host against a
Testcontainers Postgres, applies all migrations, sets `Messaging:SoloMode=true` (so durable events drain), and
gives you: `Client`, `RegisterAndLoginAsync(email,pw) → (userId, accessToken)`, `Authed(method,url,token,body?)`,
`ExecuteSqlAsync`, `ScalarAsync<T>`, `WaitForCountAsync(countSql, n)`. Your module's `*.Tests` references it and
uses `IClassFixture<PlatformApiFactory>`.

**Templates to copy** (don't reinvent): `Billing.Tests/BillingConcurrencyTests` (no-double-spend, 20-way),
`BillingLedgerTests` (confirm exactly-once, idempotent top-up), `CrossModuleEventTests` (event → handler →
side-effect, with `WaitForCountAsync`), `Identity.Tests/IdentityE2ETests`, `Gdpr.Tests/SubjectKeyShredTests` (pure unit).

## Four layers
1. **Architecture (ArchUnitNET)** — `tests/ModularPlatform.ArchitectureTests`. Add new module assemblies to
   `LoadAssemblies(...)`; the rules (Contracts pure, no cross-module Core) auto-cover them. New integration events →
   add to `MessageWireIdentityTests.FrozenWireNames` (freezes durable wire identity). Must stay green.
2. **Integration** — via `PlatformApiFactory`. Drive real HTTP endpoints (or seed DB state via `ExecuteSqlAsync`
   for preconditions that have no command). For event-driven setup, `WaitForCountAsync` until the handler ran.
   Assert on the response AND the DB state. A derived host with overrides (low rate limit, Production env) = `fixture.CreateHost((k,v)…)`.
3. **Host boot (DI graph)** — `tests/ModularPlatform.Hosts.Tests` builds the Worker/Jobs/Migration hosts in Development
   (ValidateOnBuild + ValidateScopes) WITHOUT starting them, so an unfulfillable/captive DI graph fails as a test, not
   in prod. Add `--Modules:{Name}:Enabled=true` to `BootArgs()` when you add a module. (Separate process — never boots the Api,
   so the one-host-per-process PII-protector invariant holds.)
4. **Pure unit (no host)** — `tests/ModularPlatform.BuildingBlocks.Tests` (building-block logic: option validators, IP
   masking, paging clamps) + per-module pure helpers (validators' `errorCode`, crypto-shred). xUnit + `Shouldly`, no DB.

## Must-cover scenarios (the ones that bite)
- **No double-spend**: N concurrent reservations on a fixed balance → exactly the affordable count succeed, rest 422, available never negative.
- **Idempotency**: same key / same Stripe event / redelivered message applied → exactly ONE effect.
- **Cross-module event**: register a user → the consuming module's side-effect appears (`WaitForCountAsync`).
- **Audit**: after an update, the audit row holds ONLY changed fields (value-converted, e.g. enum → string).
- **Auth**: refresh-token reuse revokes the family (401) + writes an audit row; lockout after N failures; reused/expired/erased token → 401; login is timing-equalized (unknown email ≡ wrong password, same code).
- **GDPR**: erasure blanks/deletes PII per module + shreds the subject key; the ledger is retained; consent is exported + deleted.
- **Authz/IDOR**: a foreign user's resource (file/operation) → 404 even with RLS off (app-level `UserId` filter); identity comes from the token, never a body/route id.
- **Edge/abuse**: rate limit partitions per-user (not one shared bucket) + per-IP on auth/register; a startup misconfig (weak JWT key, empty forwarded-headers trust list, fake Stripe in Production) fail-fasts via its `IValidateOptions`.

## Conventions
- One behavior per test. Arrange via real commands/HTTP where one exists, not by poking the DB.
- Never mock the DbContext — use the container. Assert through queries/responses, never private state.
- A money/concurrency test fires commands with `Task.WhenAll` over the SHARED host (don't wrap in a test-owned
  transaction — that would hide the real concurrency).

## Verify
`dotnet test` green (incl. ArchUnitNET).
