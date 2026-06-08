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
2. **Commands mutate; queries read.** Commands run the full pipeline (validation → idempotency → transaction →
   outbox → concurrency-retry). Queries NEVER open a transaction, NEVER publish, NEVER mutate.
3. **Modules talk to each other ONLY through `*.Contracts`** (integration events + DTOs). A module's `Core` is
   `internal`. **Never** reference another module's Core type. **Never** JOIN across modules — reference by Id.
4. **Don't reinvent what the platform already solved (§4).** Outbox, inbox dedup, audit, concurrency, error
   translation, refresh-token rotation, the credit ledger — all exist. Use them.
5. **Only free, battle-tested libraries** (§6). No MediatR (commercial), no MassTransit v9 (commercial).
6. **All times UTC** (`IClock.UtcNow`, `DateTimeOffset`). Never `DateTime.Now`.
7. **Never run migrations against a shared DB.** Use a local/Testcontainers Postgres or a per-branch clone.

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
src/hosts/
  ModularPlatform.Api               HTTP host; discovers modules; maps endpoints; publishes
  ModularPlatform.Worker            Wolverine listener; runs dispatched commands + integration-event handlers
  ModularPlatform.Jobs              Quartz CRON ONLY (reconciliation, retention, expiry)
  ModularPlatform.MigrationService  applies every module's migrations, then exits
tests/ModularPlatform.ArchitectureTests  ArchUnitNET — fails the build if a module boundary is violated
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
   `ITenantScoped` / `ISoftDeletable` as needed. Add an `IEntityTypeConfiguration<T>` (table name snake_case).
   **No navigation properties** — reference other entities by `Guid` Id.
4. DbContext: derive from `PlatformDbContext`, override `ModuleName`, expose `DbSet`s. ctor must be
   `(DbContextOptions<T> options, ITenantContext tenant)`. Add an `IDesignTimeDbContextFactory<T>` (copy
   `IdentityDbContextDesignTimeFactory`).
5. Implement `IModule` (copy `IdentityModule`): in `RegisterServices` call `AddCqrs(assembly)`,
   `AddValidatorsFromAssembly(assembly, includeInternalTypes:true)`, `AddModuleDbContext<T>(Name, writeConn)`,
   `AddModuleReadDbContext<T>(readConn)`, plus module services. `MapEndpoints` calls each endpoint extension.
   `ApplyMigrationsAsync` migrates the module's context.
6. Register the module's assembly in the hosts' `ModuleLoader.Discover(...)` call (Api/Worker/MigrationService
   Program.cs) **and** in `ArchitectureTests` `LoadAssemblies(...)`.
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
| **Optimistic concurrency** | Postgres `xmin` (applied to every `Entity` by convention) + `ConcurrencyRetryBehavior` (3× backoff) | nothing — just mutate entities | add a manual RowVersion column; catch concurrency yourself |
| **Audit (only changed fields → JSONB)** | `AuditInterceptor` (per-module `{module}_audit_entries`) | nothing — automatic on SaveChanges | write your own change log |
| **Errors → HTTP** | throw a `ModularPlatformException` subclass (`NotFoundException`, `ConflictException`, `ForbiddenException`, `UnauthorizedException`, `BusinessRuleException`, `ValidationException`) | `throw new ConflictException("user.email_taken", "...")` | return `BadRequest()`/`Problem()` from endpoints; build ProblemDetails yourself |
| **Error translation (i18n)** | `GlobalExceptionMiddleware` + `IStringLocalizer<SharedResource>`, **resx key == errorCode** | add the errorCode to `Web/Localization/SharedResource.resx` (+ `.cs.resx` for Czech) | translate on the client; hardcode messages |
| **Success envelope** | `ApiResponse<T>.Ok(data)` | wrap successful results | wrap errors in ApiResponse (errors are RFC 9457) |
| **Tenant scoping** | `ITenantContext` (JWT claim) + global query filter + Postgres RLS | mark entity `ITenantScoped` | read tenant from anywhere but `ITenantContext` |
| **Auth / JWT / refresh rotation + reuse detection** | Identity module (`TokenIssuer`, `RefreshTokenHandler`) | reuse Identity; don't duplicate auth | parse/issue JWTs in other modules |
| **Real-time push** | SSE (`Web/Sse/SseStream`, .NET 10 native) + `IRealtimePublisher` (Redis fan-out) | inject `IRealtimePublisher`, `PublishToUserAsync(...)` | open your own WebSocket; bypass the publisher |
| **Dispatch work to the worker + await** | Wolverine `IMessageBus.InvokeAsync<T>(cmd)` | for SHORT work; for LONG work return `202` + a status endpoint | hold an HTTP request open across long work |
| **Recurring/cron jobs** | Quartz in the **Jobs** host | add a Quartz job (reconciliation/retention/expiry) | put cron in the Worker; put durable event work in Jobs |
| **Credits / money** | Billing append-only ledger + **EF-native atomic `ExecuteUpdate` guard** on the debit path (`WHERE available >= amount`) | reuse Billing commands; invariant `available = posted - pending` | raw SQL; mutate balances by read-then-write; trust a stale balance; double-credit (use the UNIQUE idempotency key) |
| **GDPR erasure** | crypto-shredding (per-subject key) + anonymization fallback + `UserErasureRequested` event | implement `IExport/IErasePersonalData` per module | physically delete rows from append-only audit |

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

---

## 9b. Modules currently in the base

| Module | What it owns | Talks to others via |
|---|---|---|
| **Identity** | users, JWT, refresh rotation+reuse detection, profile | publishes `UserRegisteredIntegrationEvent` |
| **Billing** | append-only credit ledger (EF-native atomic debit), Stripe webhook+reconcile, packages | consumes `UserRegistered` (creates account); publishes `CreditsToppedUp`/`CreditsSpent`; implements `IExportPersonalData` |
| **Notifications** | `SendNotification` (email/push/in-app) via outbox+Worker, templates, in-app feed | consumes `UserRegistered` (welcome); uses `IRealtimePublisher`; implements `IExportPersonalData` |
| **Gdpr** | export fan-out, erasure event, consent, crypto-shredder | depends ONLY on `IExport`/`IErasePersonalData` ports — never on a module Core |

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
