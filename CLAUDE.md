# CLAUDE.md — ModularPlatform

This file is the **single source of truth** for how this codebase works. It is loaded into every Claude
Code turn. **Read it before writing any code. Do NOT invent new flows — every pattern below already exists
and has a canonical reference implementation. If something seems missing, copy the canonical example, don't
design a parallel mechanism.**

> ModularPlatform = a production-grade **.NET 10 SaaS base** (users, billing/credits, audit, GDPR, i18n,
> notifications, durable messaging). **Modules** add product "flavor" on top. Architecture decisions are
> frozen in `~/.claude/plans/ultracode-tohle-budem-musel-transient-papert.md` and the Wolverine primer on the
> Desktop (`~/Desktop/Wolverine-navod.md`).

---

## 0. THE LAWS (never break these)

1. **Everything is a `ICommand<T>` or `IQuery<T>`. There are NO generic "services".** Business logic lives in a
   handler inside a vertical slice `Features/{Feature}/{Action}/`. If you're tempted to write a `FooService`,
   you're doing it wrong — make it a command or query.
2. **Commands mutate; queries read.** The CQRS pipeline behaviors are Telemetry → Logging → Validation →
   ConcurrencyRetry (command-only). **Idempotency and the transaction/outbox commit are the HANDLER's job**, not
   free pipeline steps: a write that publishes uses `IDbContextOutbox.SaveChangesAndFlushMessagesAsync` (that IS the
   commit); idempotency is a UNIQUE key + `catch (DbUpdateException)`. Queries NEVER open a transaction / publish / mutate.
3. **Modules talk to each other ONLY through `*.Contracts`** (integration events + DTOs). A module's `Core` is
   `internal`. **Never** reference another module's Core type. **Never** JOIN across modules — reference by Id.
4. **REUSE-FIRST / DRY.** Don't reinvent what the platform already solved (§4): outbox, inbox dedup, audit,
   concurrency, error translation, refresh-token rotation, the credit ledger, the test harness — all exist. **Copy
   the canonical slice (§2), call the building-block, chain a command — never duplicate logic or re-implement a
   solved concern.** When two handlers need the same behavior, one dispatches the other; you do not copy-paste.
5. **EF / LINQ only — NEVER raw SQL.** Pessimistic guard = atomic `ExecuteUpdate` with a `WHERE`; otherwise xmin +
   `ConcurrencyRetryBehavior`; idempotency = UNIQUE key + catch `DbUpdateException`.
6. **Only free, battle-tested libraries** (§6). No MediatR (commercial), no MassTransit v9 (commercial).
7. **All times UTC** (`IClock.UtcNow`, `DateTimeOffset`). Never `DateTime.Now`.
8. **Never run migrations against a shared DB.** Use a local/Testcontainers Postgres or a per-branch clone.
9. **Tests reuse the shared harness** `tests/ModularPlatform.IntegrationTesting` (`PlatformApiFactory`) — never write
   a new Testcontainers fixture. See the `writing-modularplatform-tests` skill.
10. **Identity ALWAYS comes from the token** (`ITenantContext.UserId`), NEVER from a route/body id — a client-supplied
   subject id is an IDOR. (Billing + the GDPR `/gdpr/me/*` endpoints do this; copy them.)
11. **A concern is "solved" only if a working canonical example exists (§2/§4).** If it's listed in §10 "NOT YET",
   there is NO pattern — **stop and ask the user for the decision; do not invent a parallel flow.**

---

## 1. Solution shape

```
src/building-blocks/   ← the platform (don't put business logic here)
  ModularPlatform.Cqrs          ICommand/IQuery/IDispatcher + the thin dispatcher + Validation/Logging behaviors + error types
  ModularPlatform.Abstractions  IModule, ITenantContext, IClock, IRealtimePublisher, IExport/IErasePersonalData, ModuleLoader, IIntegrationEvent(*)
  ModularPlatform.Persistence   PlatformDbContext base, AuditInterceptor, ConcurrencyRetryBehavior, conventions, read factory
  ModularPlatform.Messaging     Wolverine wiring + AddModuleDbContext (write context w/ EF outbox)
  ModularPlatform.Web           RFC9457 errors, ApiResponse<T>, i18n (resx), SSE, JWT, RateLimiter, HttpTenantContext
  ModularPlatform.Telemetry     OpenTelemetry + TelemetryBehavior
src/modules/{Name}/    ← a module = a trio
  ModularPlatform.{Name}            Core (internal) — entities, DbContext, features, IModule impl
  ModularPlatform.{Name}.Contracts  public — integration events + shared DTOs ONLY (refs Cqrs only)
  ModularPlatform.{Name}.Tests
src/hosts/   ← each non-Api host's composition is extracted into a `{Host}HostBuilder.Create(args)` so a boot test can
             validate its exact DI graph; all four hosts Discover the SAME full module set
  ModularPlatform.Api               HTTP host; discovers modules; maps endpoints; publishes
  ModularPlatform.Worker            Wolverine listener (THE consumer); runs dispatched commands + integration-event handlers (WorkerHostBuilder)
  ModularPlatform.Jobs              Quartz CRON ONLY (reconciliation, retention, expiry); pure publisher (JobsHostBuilder)
  ModularPlatform.MigrationService  applies every module's migrations + RLS, then exits (MigrationHostBuilder)
tests/ModularPlatform.ArchitectureTests  ArchUnitNET boundary rules + MessageWireIdentityTests (frozen event wire names)
tests/ModularPlatform.Hosts.Tests         boots Worker/Jobs/Migration (Build-only) → ValidateOnBuild catches an unfulfillable DI graph
tests/ModularPlatform.BuildingBlocks.Tests pure-unit for building blocks (option validators, IP masking, paging)
```
(*) `IIntegrationEvent` lives in `Cqrs` so `*.Contracts` (which references only `Cqrs`) can define events.

**Reference graph (enforced by ArchUnitNET):** module `Core` → building-blocks + any `*.Contracts`. Module
`Contracts` → `Cqrs` only. Hosts → modules + building-blocks. **`Core` never references another `Core`.**

---

## 2. THE CANONICAL EXAMPLE — copy this, don't improvise

The **Identity module** is the reference implementation of every pattern. When adding anything, open these files
and mirror them exactly:

| You're writing… | Copy this file |
|---|---|
| A write command (mutate + publish event) | `src/modules/Identity/…/Features/Users/RegisterUser/RegisterUserHandler.cs` |
| A read query | `src/modules/Identity/…/Features/Users/GetProfile/GetProfileHandler.cs` |
| A validator | `…/Features/Users/RegisterUser/RegisterUserValidator.cs` |
| An endpoint | `…/Features/Users/RegisterUser/RegisterUserEndpoint.cs` |
| A command/response/request record | `…/Features/Users/RegisterUser/RegisterUserCommand.cs` |
| An entity + config | `src/modules/Identity/…/Entities/User.cs` |
| A module DbContext | `…/Persistence/IdentityDbContext.cs` |
| The `IModule` wiring | `src/modules/Identity/…/IdentityModule.cs` |
| An integration event | `src/modules/Identity/ModularPlatform.Identity.Contracts/IntegrationEvents.cs` |
| A security-sensitive flow (token rotation/reuse) | `…/Features/Auth/RefreshToken/RefreshTokenHandler.cs` |

### A vertical slice = one folder, these files
```
Features/{Feature}/{Action}/
  {Action}Command.cs    // the record(s): Command/Query + Response + wire Request. (sealed records)
  {Action}Validator.cs  // FluentValidation, .WithErrorCode("dotted.code")
  {Action}Handler.cs    // the ONLY place business logic lives
  {Action}Endpoint.cs   // Minimal API extension method; maps request→command, calls IDispatcher, wraps ApiResponse<T>
```
Endpoints do **no** business logic and **no** error handling. The dispatcher + behaviors + global exception
middleware handle validation, transactions, concurrency, and error translation.

---

## 3. How to ADD A MODULE (the only correct way)

1. Create the trio:
   `src/modules/{Name}/ModularPlatform.{Name}` (classlib), `…/.{Name}.Contracts` (classlib), `…/.{Name}.Tests` (xunit).
   - `.{Name}` references: Cqrs, Abstractions, Persistence, Messaging, Web (+ `Microsoft.EntityFrameworkCore.Design`), `.{Name}.Contracts`.
   - `.{Name}.Contracts` references: Cqrs **only**.
2. Make **all Core types `internal`**. The only public types are in `.{Name}.Contracts`.
3. Entities: derive from `Entity` or `AuditableEntity` (`ModularPlatform.Persistence.Entities`). Mark
   `ITenantScoped` / `ISoftDeletable` / `IUserOwned` as needed (`IUserOwned` → a `Guid UserId` column + automatic
   per-user RLS). Add an `IEntityTypeConfiguration<T>` (table name snake_case).
   **No navigation properties** — reference other entities by `Guid` Id.
4. DbContext: derive from `PlatformDbContext`, override `ModuleName`, expose `DbSet`s. ctor must be
   `(DbContextOptions<T> options, ITenantContext tenant)`. Add an `IDesignTimeDbContextFactory<T>` (copy
   `IdentityDbContextDesignTimeFactory`).
5. Implement `IModule` (copy `IdentityModule`): in `RegisterServices` call `AddCqrs(assembly)`,
   `AddValidatorsFromAssembly(assembly, includeInternalTypes:true)`, `AddModuleDbContext<T>(Name, writeConn)`,
   `AddModuleReadDbContext<T>(readConn)`, plus module services. `MapEndpoints` calls each endpoint extension.
   **Endpoints map RELATIVE routes** (`/operations/demo`, not `/v1/operations/demo`): the Api host creates the
   single `/v1` versioning group once (`app.MapGroup("/v1")`) and passes it as the `IEndpointRouteBuilder` to every
   `MapEndpoints(...)`, so every module endpoint is served under `/v1` automatically. Never hardcode `/v1` in a
   module; never build a `Location`/link by string-concatenating the path — use the named route + `LinkGenerator`
   so it stays correct under the group prefix (see `StartDemoOperationEndpoint`).
   `ApplyMigrationsAsync` migrates the module's context via `PlatformMigrator.MigrateAsync<T>(services, adminConn, Name, ct)`
   (admin connection — the DI context uses the RLS runtime role, which can't run DDL). Copy `IdentityModule`.
6. Register the module's assembly + csproj ProjectReference in **all four** host composers — `Api/Program.cs`,
   `Worker/WorkerHostBuilder.cs`, `Jobs/JobsHostBuilder.cs`, `MigrationService/MigrationHostBuilder.cs` (host composition
   is extracted into `*HostBuilder` classes so the boot test validates the real DI graph) — **and** in `ArchitectureTests`
   `LoadAssemblies(...)`, **and** `--Modules:{Name}:Enabled=true` in `Hosts.Tests` `HostBootTests.BootArgs()`. New
   integration events: add their full type names to `MessageWireIdentityTests.FrozenWireNames`.
7. Add `"Modules": { "{Name}": { "Enabled": true } }` to appsettings.
8. Generate the migration (see §7). Add tests.

A module is enabled/disabled per deployment via the `Modules:{Name}:Enabled` flag — **that is how the same base
ships as different products.**

---

## 4. WHAT IS ALREADY SOLVED — do NOT build a parallel mechanism

| Concern | Already done by | How you use it | NEVER do |
|---|---|---|---|
| **In-proc command/query dispatch** | `IDispatcher` (thin custom dispatcher, `Cqrs/Dispatcher.cs`) | `dispatcher.Send(cmd)` / `dispatcher.Query(q)` | add MediatR; call handlers directly |
| **Validation** | `ValidationBehavior` + FluentValidation | add a `{Action}Validator` with `.WithErrorCode(...)` | validate inside handlers/endpoints |
| **Transaction + outbox (publish events atomically)** | **Wolverine** EF integration | inject `IDbContextOutbox<TContext>`, do work on `.DbContext`, `PublishAsync(evt)`, then `SaveChangesAndFlushMessagesAsync()` | hand-roll an outbox table or a BackgroundService poller |
| **Message idempotency (dedup)** | **Wolverine inbox** (UNIQUE MessageId) | nothing — it's automatic for durable handlers | write your own "already processed?" table for messages |
| **Optimistic concurrency** | Postgres `xmin` (every `Entity`) + `ConcurrencyRetryBehavior` (5× backoff, clears tracker before retry) | nothing — just mutate tracked entities | manual RowVersion; catch concurrency yourself |
| **Audit (only changed fields → JSONB)** | `AuditInterceptor` on **SaveChanges** → per-module `{module}_audit_entries` | nothing — automatic on `SaveChanges` | write your own change log. **CAVEAT: `ExecuteUpdate`/`ExecuteDelete` BYPASS the interceptor + xmin** — use them only where the change need not be audited (e.g. the ledger debit guard, whose `credit_entries` ARE the audit; GDPR scrubs). |
| **Audit-PII crypto-shred** | mark a string property `[PersonalData]` + the entity implements `IDataSubject` (`ModularPlatform.Abstractions`). `AuditInterceptor` encrypts those audit values under the subject's DEK via the `IPersonalDataProtector` port (Gdpr impl `PersonalDataProtector` over `CryptoShredder`+`SubjectKey`); GDPR erasure shreds the DEK (existing `ShredSubjectKey`) → audit PII unrecoverable. Admin forensic read: `GET /v1/identity/admin/users/{id}/audit` (`PlatformPermissions.AuditRead`) reveals until erasure, then `[erased]`. Canonical: `User`/`Notification` + `Identity/.../Features/Audit/GetUserAuditTrail` | mark `[PersonalData]` + implement `IDataSubject` (an ArchUnitNET rule enforces the pairing) | store PII plaintext in audit; route `SubjectKey` writes through the audited context (the DEK would land in audit — the protector uses an interceptor-free context); trust an in-process DEK cache for encryption (the protector reads the live key every call) |
| **Errors → HTTP** | throw a `ModularPlatformException` subclass (`NotFoundException`, `ConflictException`, `ForbiddenException`, `UnauthorizedException`, `BusinessRuleException`, `ValidationException`) | `throw new ConflictException("user.email_taken", "...")` | return `BadRequest()`/`Problem()` from endpoints; build ProblemDetails yourself |
| **Error translation (i18n)** | `GlobalExceptionMiddleware` + `IStringLocalizer<SharedResource>`, **resx key == errorCode** | add the errorCode to `Web/Localization/SharedResource.resx` (+ `.cs.resx` for Czech) | translate on the client; hardcode messages |
| **Success envelope** | `ApiResponse<T>.Ok(data)` | wrap successful results | wrap errors in ApiResponse (errors are RFC 9457) |
| **Tenant scoping (read filter)** | `ITenantContext` (JWT `tenant_id` claim) + EF global query filter (`IsSystem ‖ TenantId == claim`); `TenantStampingInterceptor` stamps `TenantId` on insert. Multi-tenant IS LIVE — registration provisions a `Tenant`, stamps the user, and the token carries the claim | mark entity `ITenantScoped` | read tenant from anywhere but `ITenantContext` |
| **Per-user data isolation (defence-in-depth)** | **Postgres RLS** keyed on `app.principal_id` GUC, applied by `RlsBootstrapper` to every `IUserOwned` table; the app's DATA connections use the least-privilege `app_rls` role (set by `RlsConnectionString`), `PrincipalSessionConnectionInterceptor` stamps the GUCs from the token. A forgotten `WHERE UserId == …` still cannot leak rows | mark a per-user entity `IUserOwned` (it must have a `Guid UserId`) — RLS is then automatic | connect the app as a superuser/owner (bypasses RLS); run migrations on the `app_rls` role (use `PlatformMigrator` on the admin connection) |
| **Auth / JWT / refresh rotation + reuse detection** | Identity module (`TokenIssuer`, `RefreshTokenHandler`) | reuse Identity; don't duplicate auth | parse/issue JWTs in other modules |
| **Authorization (roles + permissions)** | Role/Permission/UserRole/RolePermission in Identity; the token carries `role` + `permission` claims (snapshot, refreshed on re-auth); `IdentitySeeder` seeds permissions from `PlatformPermissions` + the system `admin` role; first admin bootstrapped via `Identity:Auth:AdminEmails` (on login) | gate an endpoint with `.RequirePermission(PlatformPermissions.X)` (preferred) or `.RequireRole("admin")`; add a new permission as a `PlatformPermissions` const (auto-seeded, auto-granted to admin) | check roles/permissions by hand; hit the DB per request (claims already carry them); invent a parallel role store |
| **Real-time push (producer + SSE)** | `IRealtimePublisher` (Redis fan-out) for producers; the browser stream is `GET /realtime/stream` (`MapRealtimeStream`, .NET 10 native SSE, owner-scoped via the token). Non-transactional pushes fire AFTER commit | inject `IRealtimePublisher`, `PublishToUserAsync(...)`; clients consume `/realtime/stream` | open your own WebSocket; publish before the DB commit (a denied write would emit a phantom event) |
| **Recurring/cron jobs** | Jobs host discovers modules + Quartz; a module schedules jobs in `IModule.RegisterJobs(quartz, config)`. A job is a thin `IJob` that dispatches a command. Canonical: `BillingExpireCreditsJob` (cron `Modules:Billing:Jobs:ExpireCreditsCron`) | implement `RegisterJobs`; add an `IJob` that `dispatcher.Send(...)` | put durable event work in cron (it belongs in the Worker via Wolverine); do logic in the job (it goes in the handler) |
| **Messaging resilience (retry/DLQ)** | `PlatformMessaging` configures retry-with-cooldown → durable dead-letter for any handler exception (inbox dedup keeps it ~exactly-once) | nothing — automatic for durable handlers | swallow handler exceptions; build a custom retry loop. Per-external-system drift = a module reconciliation **job**, not a generic reconciler |
| **Long-running command (202 + status)** | Operations module: `IOperationStore` + the `operations` table (RLS-isolated). Accept → create operation + publish durable work (outbox) → return **202** + `Location: /operations/{id}`; worker transitions it; caller polls. Canonical: `StartDemoOperation` + `RunDemoOperationHandler` | inject `IOperationStore`, `CreateAsync` then publish your work message; complete from the worker; reply 202; map status via GET `/operations/{id}` | hold the HTTP request open for slow work; do the work in the accept handler |
| **Credits / money** | Billing append-only ledger + **EF-native atomic `ExecuteUpdate` guard** on the debit path (`WHERE available >= amount`) | reuse Billing commands; invariant `available = posted - pending` | raw SQL; mutate balances by read-then-write; trust a stale balance; double-credit (use the UNIQUE idempotency key) |
| **Stripe commerce (packages, subscriptions, coupons, Tax)** | `IStripeGateway` port (Billing Core, `Stripe/IStripeGateway.cs`) — the ONE Stripe seam (real `StripeGateway` / in-memory `FakeStripeGateway` under `Billing:Stripe:UseFakeGateway`, test harness only). Packages = DB catalogue + Checkout (`PurchaseCreditPackage`); subscriptions = CONFIG plans (`Billing:Subscriptions:Plans`) mirrored ONLY from Stripe object state (`UpsertSubscriptionFromStripe` — out-of-order safe); per-period grant = `sub-invoice:{invoiceId}` idempotency; coupons/Tax = `AllowPromotionCodes`/`AutomaticTax` flags | call Stripe ONLY through `IStripeGateway`; route new webhook types in `ProcessStripeEventCommand`; admin catalogue via `billing.manage` | call Stripe.net services directly; trust webhook payload order (always refetch object state); pre-create local subscription rows from checkout |
| **Saga / multi-step self-healing workflow** | **Wolverine saga** (EF-persisted in the module DbContext via `UseEntityFrameworkCoreTransactions`). Canonical: `Billing/Sagas/CreditPurchaseSaga` — Start via outboxed message, `TimeoutMessage` abandon, money granted ONLY through the existing idempotent command, late confirmation honored via static `NotFound`, saga row doubles as the user-facing record (deliberately no `MarkCompleted`) | copy `CreditPurchaseSaga`: public class, `Discovery.IncludeType`, messages carry `Id` (saga identity), guard terminal states by `Status` | mutate money inside the saga; rely on exceptions for compensation; a second dedup table (reuse UNIQUE idempotency keys) |
| **External-system reconciliation job** | canonical `ReconcileStripeCommand` (+`BillingStripeReconcileJob`, cron `Modules:Billing:Jobs:ReconcileStripeCron`): re-queues stuck `stripe_events` via the outbox + corrects subscription drift against LIVE Stripe state (Stripe wins); caps per run; drift → WARN log + `platform.billing.stripe_drift` counter | copy the two-pass sweep for any external system | a generic cross-module reconciler; raw SQL over state tables |
| **Stuck-outbox / dead-letter alert** | `MessagingHealthJob` (Jobs HOST — platform concern): Wolverine `IMessageStore.Admin.FetchCountsAsync()` → OTel gauges `platform.messaging.{dead_letters,incoming_pending,outgoing_pending}` + WARN over `Messaging:StuckThreshold`; cron `Messaging:HealthCheckCron` | nothing — runs in every Jobs deployment | SQL over `wolverine.*` tables (use the store API); paging/e-mail from jobs (alerting belongs to infrastructure) |
| **Retention sweep** | `GdprRetentionSweepJob` → `RetentionSweepCommand`: shredded `subject_keys` tombstones are **retained PERMANENTLY** as the DEK re-mint guard (deleting one would let a post-erasure PII write resurrect a readable key), so the sweep currently purges NOTHING — it is the seam to copy for future module-owned, genuinely-purgeable retention data. Per-module data retention stays per-module (boundary law) | copy for module-owned retention | delete shredded tombstones; a cross-module reaper |
| **Custom metrics** | `PlatformMetrics.Meter` (Telemetry building-block, name `ModularPlatform`), exported via `.AddMeter` in `AddPlatformTelemetry`; naming `platform.{area}.{thing}` | create instruments off `PlatformMetrics.Meter` | a second Meter that is never `.AddMeter`-ed (silently unexported) |
| **PII at rest (live columns)** | `[Encrypted]` (+`[PersonalData]`, entity `IDataSubject` — ArchUnitNET-enforced): writes sealed by `PersonalDataEncryptionInterceptor` (runs AFTER audit) into `penc:v2` envelopes (AES-GCM, AAD = subjectId) under the per-subject DEK; reads decrypted by a model-level converter (works on the read factory; shredded → `[erased]`). Lookups on encrypted values = `IBlindIndexHasher` keyed HMAC (`Gdpr:Encryption:BlindIndexKey`, fail-fast outside Dev) — canonical `users.EmailHash` (UNIQUE, login/duplicate checks). Erasure: tombstone + blanked `PasswordHash` + DEK shred kills the ciphertext. `PiiEncryptionBackfill` seals legacy rows | mark `[Encrypted]`; look up via the blind index, never the ciphertext column | query encrypted columns by value; per-subject keys for cross-subject lookups (login is pre-auth → platform-wide HMAC key); a second Testcontainer/host per test process (breaks the process-wide protector) |
| **Realtime replay (`Last-Event-ID`)** | Redis Streams log `rts:user:{id}` (MAXLEN ~ `Realtime:Replay:MaxEvents`, TTL `TtlMinutes`); stream id IS the SSE event id; `IRealtimeReplay.ReadSinceAsync` replays on reconnect before bridging live; bounded in-memory ring buffer fallback without Redis | nothing — automatic on `/realtime/stream` reconnects | treat replay as guaranteed delivery (it is best-effort UX smoothing; durable facts live in modules) |
| **GDPR erasure** | `UserErasureRequested` (Worker) fans out `IErasePersonalData` per module + crypto-shreds the subject key | implement `IExport/IErasePersonalData` per module + register both in `RegisterServices` | physically delete append-only ledger/audit rows (retained for AML/tax; anonymize instead) |
| **Auth hardening** | per-account lockout (`FailedAccessCount`/`LockoutEndUtc` on `User`, 5 strikes → 15 min) + per-IP `"auth"` rate-limit on `/login` `/refresh` `/users` (register); login is TIMING-EQUALIZED (always an Argon2 verify vs a dummy hash → unknown email ≡ wrong password); soft-deleted/erased accounts can't log in or refresh; family-revoke tracked+audited | reuse Identity | bulk `ExecuteUpdate` on audited security rows (bypasses audit/xmin); user-enumeration (timing or 409-vs-201) |
| **Request-edge hardening** | partitioned rate-limiting (global = per **user** via the `NameIdentifier` claim, else per IP; `"auth"` policy = per IP) with 429 + `Retry-After`; forwarded-headers proxy trust is config (`ForwardedHeaders:KnownProxies`/`KnownNetworks`, validator fail-fasts an empty trust list outside Dev — else clients spoof `X-Forwarded-For`); audit IP is minimization-configurable (`Audit:IpStorage` = `Full`\|`Truncated`/24-/48\|`None`) | tune `RateLimiting:*`; set the proxy trust list in prod | partition the global limiter by `Identity.Name` (it's null → one shared bucket); truncate the rate-limit IP (only the AUDIT IP is masked); trust forwarded headers with no `KnownProxies` |
| **Durable-envelope PII bound** | Wolverine durable envelopes carry plaintext PII the send path needs (a welcome e-mail address) and can't be crypto-shredded in place, so their LIFETIME is bounded instead: `KeepAfterMessageHandling`=5m + `DeadLetterQueueExpirationEnabled`=7d (off by default → would persist forever); Redis realtime replay is TTL+MAXLEN-bounded | nothing — `PlatformMessaging` sets it | treat dead-letters as permanent recovery (recovery is `ReconcileStripe` from live state); leave DLQ expiration off |
| **Tenant isolation** | EF global query filter `IsSystem ‖ TenantId == claim` (no null-escape). System context = `SystemTenantContext` (worker/jobs hosts) **or `HttpTenantContext` when there is no HttpContext** (background Wolverine handlers + startup inside the Api process) | mark entity `ITenantScoped` | trust a missing tenant claim as "see everything"; assume an in-Api background handler is tenant-scoped — it is SYSTEM |
| **File upload / blobs** | `IFileStorage` port (`ModularPlatform.Abstractions`) + the `ModularPlatform.Storage` building-block: `LocalFileStorage` (disk, dev) and `S3FileStorage` (AWS SDK — AWS S3 / MinIO / Cloudflare R2 via `Storage:S3:ServiceUrl`+`ForcePathStyle`), selected by `Storage:Provider` = `local`\|`s3` (default local) via `AddPlatformStorage`. The **Files** module owns file METADATA (`file_objects`, `IUserOwned`→RLS) + the upload/download/list slices | inject `IFileStorage`; persist a `FileObject` for the metadata. POST `/files` is multipart, owner from the token | use the client filename as the storage KEY (path traversal) — the key is a SERVER-GENERATED opaque id; skip the content-type allowlist or the size cap; read/stream another user's file (RLS makes a foreign id a 404) |

**File upload security (Files module, non-negotiable):** the storage key is server-generated (`{userId:N}/{id:N}`), NEVER the client filename; a content-type **allowlist** (`FileUploadPolicy`, deny by default) + a **10 MB cap** are enforced by `UploadFileValidator` (and a request-body size limit on the endpoint); the owner is `ITenantContext.UserId`; download/list are RLS-scoped (foreign id → 404). `StorageKey.Validate` is a defence-in-depth path-traversal guard on every provider call.

---

## 5. How modules COMMUNICATE

- **One-way fact → publish an integration event** from `*.Contracts` via the outbox
  (`IDbContextOutbox.PublishAsync`). Other modules add a Wolverine handler (a class with a
  `Handle(TheEvent evt, deps…)` method) in their Core — Wolverine auto-discovers it, the **Worker** runs it,
  the inbox dedups it. Canonical producer: `RegisterUserHandler` → `UserRegisteredIntegrationEvent`.
- **Need data from another module → call its query via the dispatcher** if in-process, or copy the data you need
  into your own module (denormalized, kept in sync via its events). **Never** JOIN to its tables or reference its
  Core. Everything by Id.
- **Need a result back from the worker → `IMessageBus.InvokeAsync<TResponse>(command)`** (short work only).
- **Feature chaining / dedup:** a handler may `dispatcher.Send(otherCommand)` to reuse logic. Keep each command
  single-responsibility and compose them — don't copy-paste logic between handlers.

---

## 6. Libraries (frozen choices — all free)

custom dispatcher (ours) · **Wolverine** (MIT, durable messaging/outbox/inbox/sagas, Postgres transport) ·
**FluentValidation** (Apache-2.0) · **EF Core 10 + Npgsql** · **Quartz** (cron) · **Stripe.net** (Apache-2.0) ·
**StackExchange.Redis** · **Isopoh.Cryptography.Argon2** (password hashing) · **OpenTelemetry** ·
**ArchUnitNET** + **xUnit** + **Testcontainers** (tests). Versions are pinned centrally in
`Directory.Packages.props` (Central Package Management — `PackageReference` carries **no** version).
**Banned:** MediatR (commercial v13+), MassTransit v9 (commercial), any hand-rolled queue/outbox/job-engine.

---

## 7. Commands

```bash
dotnet build                       # whole solution (warnings = errors; keep it 0/0)
dotnet test                        # all tests
dotnet test tests/ModularPlatform.ArchitectureTests   # boundary rules only (no DB needed)

# Add a migration for a module (design-time factory required):
dotnet ef migrations add <Name> \
  --project src/modules/<Mod>/ModularPlatform.<Mod>/ModularPlatform.<Mod>.csproj \
  --context <Mod>DbContext --output-dir Persistence/Migrations

# Apply all module migrations to a local DB:
dotnet run --project src/hosts/ModularPlatform.MigrationService

# Run the API / Worker / Jobs:
dotnet run --project src/hosts/ModularPlatform.Api
dotnet run --project src/hosts/ModularPlatform.Worker
dotnet run --project src/hosts/ModularPlatform.Jobs
```
Local Postgres for dev/tests: a Docker Postgres on `localhost:5432` (db `modularplatform`, `postgres/postgres`),
or Testcontainers in integration tests. **Connection strings: `ConnectionStrings:Write` and `:Read`.**

**RLS deployment:** `ConnectionStrings:Write/Read` MUST be an **admin/owner** role (it runs migrations, Wolverine
storage, and provisions the runtime role) — and because protected tables use `FORCE ROW LEVEL SECURITY`, that admin
role should have **`BYPASSRLS`** (or be superuser) so a future DATA migration touching an `IUserOwned` table isn't
row-filtered. The app's DATA connections are auto-derived to use the least-privilege `app_rls` role (no DDL, no
RLS bypass). `app_rls` is the application's runtime role, so it holds DML on all app tables (it legitimately writes
users/audit/outbox); per-row isolation on sensitive tables comes from the RLS **policies**, not from withholding
table grants. Set `Persistence:Rls:RuntimePassword` from a real secret in prod (dev/test use a placeholder).
Both the Api startup path and the dedicated `MigrationService` run `RlsBootstrapper` after migrations.
To turn RLS off (e.g. a managed DB where you can't create roles): `Persistence:Rls:Enabled=false` — the app then
uses the admin connection for data and there is no DB-level isolation (app-level filters remain).

**Test plan / coverage map:** `docs/test-scenarios.md` (Given/When/Then per module + cross-cutting, with status).
Skills: `building-modularplatform-feature`, `adding-a-module`, `adding-billing-command`, `writing-modularplatform-tests`.

---

## 8. Conventions

- **Files:** one record/class per concept; one folder per slice. `sealed` everything. `internal` for module Core.
- **Naming:** commands are imperative (`RegisterUserCommand`), queries are `Get…Query`, events past-tense
  (`UserRegisteredIntegrationEvent`), error codes dotted lowercase (`user.email_taken`).
- **DTOs:** `record`s. Wire `Request` is separate from the `Command` (the endpoint maps Request→Command).
- **IDs:** `Guid.CreateVersion7()` (time-ordered). Tables `snake_case`. No `Include`/navigation.
- **Database access = EF / LINQ ONLY. NEVER raw SQL** (`ExecuteSqlRaw/Interpolated`, `FromSql`) — unmaintainable + injection-prone. Pessimistic guard = atomic `ExecuteUpdate` with a `WHERE`; concurrency otherwise = xmin + `ConcurrencyRetryBehavior`.
- **Tenant/user/time:** only via `ITenantContext` and `IClock`. Never read claims or `DateTime.Now` directly.
- **i18n:** every user-facing error has an errorCode whose resx entry exists in `en` and `cs`.
- **API versioning (URL strategy):** every module/app endpoint is served under a single central `/v1` prefix
  (`/v1/identity/users`, `/v1/billing/...`, `/v1/realtime/stream`). The Api host builds the group once
  (`app.MapGroup("/v1")`) and hands it to every `MapEndpoints(...)` + `MapRealtimeStream(...)`; modules map RELATIVE
  routes and NEVER hardcode `/v1`. Health checks (`/health/*`) and OpenAPI stay UNVERSIONED at root. A breaking
  revision = add a `/v2` group in the host.
- **Secrets:** layer `IConfiguration` — env vars + `dotnet user-secrets` (dev); a prod secret store (KeyVault/Secrets Manager) just feeds env/config, no SDK lock-in. Sensitive settings **fail-fast at startup** outside Development: `JwtOptionsValidator` (signing key) and `RlsBootstrapper` (refuses the dev `RuntimePassword`). Never commit a real secret; never read a secret outside its `Options` type.

---

## 9b. Modules currently in the base

| Module | What it owns | Talks to others via |
|---|---|---|
| **Identity** | users, JWT, refresh rotation+reuse detection, profile | publishes `UserRegisteredIntegrationEvent` |
| **Billing** | credit ledger (EF-native atomic debit), Stripe webhook ingest + event router, packages catalogue + Checkout purchase (CreditPurchaseSaga), config-driven subscriptions (object-state mirror, per-invoice grants), promo-code validation, Stripe Tax flag, reconcile job | consumes `UserRegistered`; publishes `CreditsToppedUp`/`CreditsSpent`/`CreditPurchaseCompleted`/`SubscriptionActivated`/`SubscriptionCanceled`; implements `IExportPersonalData` |
| **Notifications** | `SendNotification` (email/push/in-app) via outbox+Worker, templates, in-app feed | consumes `UserRegistered` (welcome); uses `IRealtimePublisher`; implements `IExportPersonalData` |
| **Gdpr** | export fan-out, erasure event, consent, crypto-shredder | depends ONLY on `IExport`/`IErasePersonalData` ports — never on a module Core |
| **Operations** | the `operations` table + the long-running 202/status mechanism; a canonical demo (POST `/operations/demo` → durable worker → GET `/operations/{id}`) | exposes `IOperationStore` (any module creates/completes operations through it); operations are `IUserOwned` (RLS-isolated) |
| **Files** | file METADATA (`file_objects`, `IUserOwned`→RLS) + upload (multipart, allowlist+size cap)/download (stream)/list (paged) slices; bytes via the `IFileStorage` building-block (local\|s3) | none — self-contained; uses the `IFileStorage` port; no cross-module events |

Notes that bite if forgotten:
- **GDPR fan-out works only if PII modules register their port impls** (`AddScoped<IExportPersonalData, XExporter>()` in their `RegisterServices`). Billing + Notifications already do.
- **Notifications welcome needs a `NotificationTemplate` row** `Key="welcome", Locale="en"` seeded, else the welcome handler throws `notification.template_not_found` (non-fatal — retried/dead-lettered).
- **Audit is NOT a separate module** — it's a platform capability (`AuditInterceptor` writes per-module `{module}_audit_entries`). A central "Audit module" reading every module's tables would violate the boundary law, so don't build one; expose per-module audit queries if needed.
- **Realtime fan-out** lives in the `ModularPlatform.Realtime` building-block (Redis pub/sub + per-instance registry; local-only fallback without Redis). The browser SSE endpoint is a host follow-up.
### Wolverine cross-module events — the EXACT working setup (don't change blindly)

Cross-module integration events (e.g. `UserRegisteredIntegrationEvent` → Billing provisions a credit account)
work via Wolverine's durable outbox/inbox. Three things are REQUIRED — all are configured in
`ModularPlatform.Messaging/PlatformMessaging.cs` + module `ConfigureMessaging`. If events stop firing, check these:
1. **`options.ServiceLocationPolicy = ServiceLocationPolicy.AlwaysAllowed;`** — Wolverine 6 defaults to
   `NotAllowed`, which SILENTLY skips generating any handler that resolves a scoped service (our handlers inject
   `IDispatcher`). Symptom when wrong: the event is marked `Handled` in `wolverine_incoming_envelopes` but the
   handler never runs, no dead letter, no side effect. **This is the #1 gotcha.**
2. **Durability mode:** single-node hosts (tests, single-instance deploy) must run `DurabilityMode.Solo`
   (`Messaging:SoloMode=true`) or the leadership-gated agent never drains the durable queue. Api+Worker multi-node
   → `Balanced` (false) on both. The integration-test fixture sets `Messaging:SoloMode=true`.
3. **Handlers are `public`** (Wolverine scans `ExportedTypes`); every type in their `Handle(...)` signature is
   public too (so the abstractions they inject, e.g. `IEmailSender`, are public). Each module registers its
   handlers explicitly in `ConfigureMessaging` via `options.Discovery.IncludeType<TheHandler>()`.

A cross-module event handler is a thin public shell: `Handle(TEvent e, IDispatcher d, CancellationToken ct)` that
dispatches an internal command. Proven end-to-end by `CrossModuleEventTests`.

### Money correctness (Billing) — EF-native, NEVER raw SQL
- **No raw SQL anywhere** (no `ExecuteSqlRaw/Interpolated`, no `FromSql`) — it's unmaintainable (silent breakage on
  rename) and an injection surface. Use EF/LINQ.
- The credit projection columns `posted`/`pending`/`available` are **authoritative**, maintained transactionally;
  the invariant **`available = posted − pending`** is preserved by arithmetic in every handler. `GetCreditBalance`
  returns the stored `available` (so the shown balance == what a reservation will allow).
- **DEBIT path = atomic conditional `ExecuteUpdate` guard** (the EF-native pessimistic equivalent):
  `db.CreditAccounts.Where(a => a.Id == id && a.Available >= amount).ExecuteUpdateAsync(s => s.SetProperty(a => a.Available, a => a.Available - amount)…)`.
  The UPDATE locks the row and evaluates the guard atomically → concurrent reservations serialize at the DB with
  **no double-spend and no retry storm**. `rows == 0` ⇒ insufficient.
- **Confirm/release/expire/top-up** mutate **tracked** entities so the **xmin** token serializes; the
  `ConcurrencyRetryBehavior` (5×, clears the change tracker before each retry) handles conflicts. Idempotency is the
  **UNIQUE `credit_entries.idempotency_key`**; catch `DbUpdateException` (not `DbUpdateConcurrencyException`) and
  return the already-applied state. Outbox handlers: `SaveChangesAndFlushMessagesAsync` IS the commit — never also
  call `tx.CommitAsync`.
- Proven by `BillingConcurrencyTests` (no double-spend, 20-way) + `BillingLedgerTests` (confirm exactly-once,
  idempotent top-up).

## 9. Before you say "done" (checklist)

- [ ] `dotnet build` is 0 warnings / 0 errors.
- [ ] New errorCodes added to `SharedResource.resx` + `SharedResource.cs.resx`.
- [ ] Mutation publishes events via the outbox (not fire-and-forget); long work returns 202.
- [ ] Read used `IReadDbContextFactory`; write used `IDbContextOutbox`/scoped context.
- [ ] No cross-module Core reference, no cross-module JOIN (ArchUnitNET green).
- [ ] Migration generated + a test (slice + boundary) added.
- [ ] No raw SQL (EF/LINQ only). Money debit path uses the atomic `ExecuteUpdate` guard; append-only ledger; idempotency via UNIQUE key.
- [ ] Identity from the token (`ITenantContext.UserId`), never a route/body id.

## 10. NOT YET SOLVED — there is NO pattern; ASK before inventing

The full planned scope is BUILT (`docs/ROADMAP.md`; §4 has the canonical examples). The concerns below have
**no canonical example** — if a task needs one, **stop and ask the user for the decision** (Law 11):

| Concern | State | Decision needed |
|---|---|---|
| **B2B subdomain multi-tenancy + per-tenant module entitlements + provisioning** | **DESIGNED, not built** — see `docs/multitenancy-and-infra.md` (subdomain tenant + `admin.` platform-admin; `tenant_entitlements`+`ModuleEntitlementGuard`→404; Caddy wildcard DNS-01; pool→silo via placement-driven connection; Pulumi for separation). Today = per-user RLS + per-tenant filter (intra-tenant) | follow the doc; don't invent a parallel flow |
| **Search**, **feature flags**, **bulk ops** | none | per need |
| **KEK/KMS envelope-wrapping of DEKs** | dev stores raw DEK in `subject_keys` | KMS choice before GA |
| **LemonSqueezy (second payment provider)** | `IStripeGateway` is the shape a provider port would mirror | only if a deployment needs it |
| **Aspire dev inner-loop** | never built (optional) | per need |
