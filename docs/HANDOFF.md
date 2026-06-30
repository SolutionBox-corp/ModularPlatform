# HANDOFF — ModularPlatform

> For the next agent/developer who will `/clear` and start fresh. Read **`CLAUDE.md`** first — it is the
> single source of truth for every law, pattern and convention. This doc is the map; `CLAUDE.md` is the law.
>
> **Status 2026-06-11:** backend **181/181 tests, build 0/0**, pushed to `main`. Start at **`docs/README.md`** (docs
> index). Current per-feature coverage = **`feature-coverage.md`**; stabilization log = `stability-audit-2026-06-10.md`.
> **Next scope (designed, not built):** B2B subdomain multi-tenancy + per-tenant module entitlements + infra —
> `multitenancy-and-infra.md` (tenant=customer on `{tenant}.nasedomena.cz`, `admin.`=platform-admin; per-user RLS stays
> intra-tenant). **Frontend:** `~/Desktop/ModularPlatform-Frontend-Handoff.md` + skills `modularplatform-frontend` /
> `frontend-feature-slice`.

---

## 1. What this is

ModularPlatform is a **production-grade .NET 10 modular-monolith SaaS base**. The building blocks deliver the
hard, cross-cutting plumbing every SaaS needs — auth/JWT, an append-only credit/billing ledger, audit, GDPR
export/erasure, i18n error translation, multi-tenancy + Postgres RLS, durable messaging (Wolverine outbox/inbox).
**Modules** (`src/modules/{Name}`, each a Core + `.Contracts` + `.Tests` trio) add product "flavor" on top and are
toggled per deployment via `Modules:{Name}:Enabled` — the same base ships as different products. Everything is a
CQRS `ICommand<T>`/`IQuery<T>` in a vertical slice; modules talk to each other ONLY through `*.Contracts`
integration events — never a cross-module Core reference or JOIN.

---

## 2. Build / run / test (commands)

Local Postgres on `localhost:5432` (db `modularplatform`, `postgres/postgres`) for dev; **integration tests need
Docker running** (Testcontainers spins up Postgres). Connection strings: `ConnectionStrings:Write` and `:Read`.

```bash
dotnet build                                          # whole solution — keep it 0 warnings / 0 errors
dotnet test                                           # all assemblies
dotnet test tests/ModularPlatform.ArchitectureTests   # boundary rules only (no DB needed)
dotnet test --filter "FullyQualifiedName~BillingLedgerTests"   # one test class

# Add a migration for a module (design-time factory required):
dotnet ef migrations add <Name> \
  --project src/modules/<Mod>/ModularPlatform.<Mod>/ModularPlatform.<Mod>.csproj \
  --context <Mod>DbContext --output-dir Persistence/Migrations

# Apply all module migrations to a local DB, then run the hosts:
dotnet run --project src/hosts/ModularPlatform.MigrationService
dotnet run --project src/hosts/ModularPlatform.Api      # HTTP host (maps endpoints under /v1)
dotnet run --project src/hosts/ModularPlatform.Worker   # Wolverine listener (dispatched cmds + event handlers)
dotnet run --project src/hosts/ModularPlatform.Jobs     # Quartz cron host
```

**RLS deployment notes** (`CLAUDE.md` §7): `ConnectionStrings:Write/Read` MUST be an **admin/owner** role — it
runs migrations, Wolverine storage, and provisions the runtime role. The app's DATA connections are auto-derived
to use the least-privilege **`app_rls`** role. Set `Persistence:Rls:RuntimePassword` from a real secret in prod
(dev/test use a placeholder). To disable: `Persistence:Rls:Enabled=false` (app-level filters remain; no DB-level
isolation). **Never run migrations against a shared DB** — Testcontainers or a per-branch clone only.

---

## 3. What is DONE — push business logic, the plumbing just works

Each row has a canonical file to copy. The everyday slice (entity → command/query → validator → endpoint →
cross-module event → money → notification → 202 status → pagination) is done and tested.

| Concern | Status | Canonical pointer |
|---|---|---|
| CQRS vertical slice (write + query) | ✅ | `src/modules/Identity/.../Features/Users/RegisterUser/*` and `.../GetProfile/GetProfileHandler.cs` |
| Outbox events (publish atomically) | ✅ | `RegisterUserHandler.cs` → `UserRegisteredIntegrationEvent` (`Identity.Contracts/IntegrationEvents.cs`) |
| Money / append-only ledger (atomic EF debit guard) | ✅ | `src/modules/Billing/...` (skill `adding-billing-command`); `BillingConcurrencyTests`, `BillingLedgerTests` |
| RLS per-user isolation (defence-in-depth) | ✅ | mark entity `IUserOwned`; `Persistence/Rls/RlsBootstrapper.cs`, `PrincipalSessionConnectionInterceptor` |
| Multi-tenancy (live) | ✅ | EF global filter `IsSystem ‖ TenantId == claim`; `TenantStampingInterceptor`; registration provisions a `Tenant` |
| Authorization — roles + permissions | ✅ | JWT claims via `TokenIssuer`; `Web/Authorization/EndpointAuthorization.cs` (`RequirePermission`/`RequireRole`); `Abstractions/PlatformPermissions.cs`; admin slices `Identity/.../Features/Admin/{AssignRole,RevokeRole}`; `AuthzTests` |
| GDPR export / erasure fan-out | ✅ | `Gdpr` module + `IExportPersonalData`/`IErasePersonalData` ports; `CryptoShredder`; Billing+Notifications register impls |
| Long-running 202 + status polling | ✅ | `src/modules/Operations/.../Features/Demo/StartDemoOperation*` + `GetOperationStatus` route; `OperationsTests` |
| Realtime + browser SSE endpoint | ✅ | `IRealtimePublisher` (Redis fan-out); `Realtime/RealtimeStreamEndpoint.cs` mapped at `v1.MapRealtimeStream()`; `RealtimeSseTests` |
| Pagination | ✅ | `Persistence/PagedQueryExtensions.cs` + `PagedResponse<T>` (used by `Files` List slice) |
| Jobs / cron hook | ✅ | `IModule.RegisterJobs(quartz, config)`; canonical `Billing/Jobs/BillingExpireCreditsJob.cs`; `hosts/Jobs/Program.cs` |
| Messaging retry / DLQ policy | ✅ | `Messaging/PlatformMessaging.cs` (Wolverine retry + dead-letter) |
| Secrets fail-fast | ✅ | `Web/JwtOptions.cs` + `JwtOptionsValidator.cs` (validates on start — no plaintext dev key in prod) |
| File storage (local + S3/MinIO/R2) | ✅ | `Abstractions/Ports.cs` `IFileStorage`; building block `ModularPlatform.Storage`; `Files` module (`Upload`/`Download`/`List`); `Storage:Provider=local\|s3` |
| API versioning | ✅ | `hosts/Api/Program.cs` `app.MapGroup("/v1")`; modules map **relative** routes; Locations via named-route + `LinkGenerator` |
| Audit / concurrency / idempotency / error→HTTP i18n | ✅ | `AuditInterceptor`, xmin + `ConcurrencyRetryBehavior`, UNIQUE key + `catch DbUpdateException`, `GlobalExceptionMiddleware` (resx key == errorCode) — `CLAUDE.md` §4 |
| Audit-PII crypto-shred (encryption-at-rest) | ✅ | `[PersonalData]`+`IDataSubject`+`IPersonalDataProtector` (Abstractions); Gdpr `PersonalDataProtector`; admin read `GET /v1/identity/admin/users/{id}/audit` (`AuditRead`); erasure → `[erased]`; `docs/audit-pii-encryption-design.md` — `CLAUDE.md` §4 |

---

## 4. What is OPEN / next — ASK before inventing (no canonical example exists)

Law 11: if a task needs one of these, **stop and ask the user for the decision** — do not invent a parallel flow.

From `CLAUDE.md` §10:
- **Saga / multi-step self-healing workflow** — Wolverine supports it; none exists. Decision: saga vs an
  orchestrating command-chain; an example if in scope.
- **Search / feature flags / bulk ops** — none. Decision: per-need design when first required.
- **Messaging resilience completeness** — base retry/DLQ policy is wired; still missing a stuck-outbox /
  reconciliation job. Decision: reconciliation cadence + alerting.

Robustness backlog (`docs/test-scenarios.md` "Priority gaps to fill next") — the old wave items are now pinned,
including EV-4 kill-worker-mid-message durability via an out-of-process worker harness that terminates and restarts
a separate `ModularPlatform.Worker` process.

---

## 5. Key decisions already made — do NOT re-litigate

- **B2C per-user tenancy, not B2B.** Registration provisions a `Tenant` and stamps the user; isolation is per-user
  via RLS. (`CLAUDE.md` §4 "Tenant scoping" / "Per-user data isolation".)
- **Real Postgres RLS with a dedicated runtime role** (`app_rls`), not app-level filtering alone. (`CLAUDE.md` §4, §7.)
- **`/v1` URL versioning** via `app.MapGroup("/v1")` — plain ASP.NET, no `Asp.Versioning` dependency. Modules map
  relative routes; Locations use named-route + `LinkGenerator`. (`CLAUDE.md` §3, §8.)
- **`IFileStorage` port with local + S3 providers** (config-selected; S3 also covers MinIO/R2). Bytes live in the
  `ModularPlatform.Storage` building block; the `Files` module owns metadata only. (`CLAUDE.md` §4 "File upload".)
- **Secrets via config + fail-fast** (`JwtOptionsValidator` validates on start) — no cloud-SDK lock-in. (`CLAUDE.md` §10.)

---

## 6. Gotchas (these will silently bite you)

- **Wolverine cross-module events** require ALL of (`CLAUDE.md` §9b): `ServiceLocationPolicy.AlwaysAllowed` (Wolverine
  6 defaults to `NotAllowed` and SILENTLY skips handlers that inject scoped services like `IDispatcher` — event
  shows `Handled`, handler never runs, no dead letter — the #1 gotcha); `DurabilityMode.Solo` for single-node hosts
  (tests set `Messaging:SoloMode=true`) else the durable queue never drains; handlers + every type in their
  `Handle(...)` signature must be `public` and registered via `options.Discovery.IncludeType<T>()`.
- **`HttpTenantContext.IsSystem` is true when there is NO HttpContext** — so an in-Api **background** Wolverine
  handler or startup code runs as **SYSTEM**, not tenant-scoped. Don't assume it's scoped.
- **TestServer cannot assert an infinite SSE round-trip** — the SSE stream never completes, so integration tests
  assert the auth/route contract (e.g. 401 on `/v1/realtime/stream`), not a full live push.
- **`ExecuteUpdate`/`ExecuteDelete` BYPASS the `AuditInterceptor` and xmin.** Use them only where the change need
  not be audited (the ledger atomic debit guard, GDPR scrubs) — never on audited security rows.
- **Never raw SQL** (EF/LINQ only); **all times UTC** via `IClock`; **identity always from the token**
  (`ITenantContext.UserId`), never a route/body id (IDOR).

---

## 7. Test status (last full `dotnet test` — 75 total, 0 failed, 0 skipped)

Grew from 50 → 75: the robustness backlog wave (+20: ID/PL/BL/ST/GD/EV/NT) and audit-PII crypto-shred (+5).

| Assembly | Pass |
|---|---|
| Notifications | 5/5 |
| Gdpr | 8/8 |
| ArchitectureTests | 3/3 |
| Operations | 3/3 |
| Files | 15/15 |
| Billing | 29/29 |
| Identity | 12/12 |
| **Total** | **75/75** |

Build: 0 warnings / 0 errors.
