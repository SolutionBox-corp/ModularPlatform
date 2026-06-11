---
name: building-modularplatform-feature
description: Scaffold a CQRS vertical slice (command or query) in a ModularPlatform module. Use when adding an endpoint, command, query, handler, feature, or cross-module event reaction. Enforces the frozen reuse-first pattern — never invent a new flow, never duplicate what a building-block already does.
---

# Building a ModularPlatform feature

**Read `CLAUDE.md` (§0 laws, §2 canonical example, §4 "already solved") first.** A feature is a vertical slice in
`Features/{Feature}/{Action}/` — never a "service". **Reuse-first: if a building-block or canonical slice already
does it, call it — do not re-implement.**

## Decide: which module owns this? (reuse-first)
Put the slice in the module that OWNS the responsibility (Billing = end-user money; Identity = auth/users; Tenancy =
tenant lifecycle + entitlements + platform billing; Notifications; Gdpr; Operations; Files). A shared mechanism ≥2
modules need (payments, secrets, storage) is a **building-block + port**, not a slice copied into each module. Only a
genuinely new product domain justifies a **new** module (see `adding-a-module` skill §0) — never duplicate a solved
concern.

## Decide: command or query?
- **Mutates state** → `ICommand<T>` (validation → idempotency → transaction+outbox → concurrency-retry).
- **Reads only** → `IQuery<T>` (no transaction, no publish, no mutation).

## Copy the canonical slice — don't improvise
| Need | Mirror this file |
|---|---|
| Write that publishes an event | `Identity/…/Features/Users/RegisterUser/RegisterUserHandler.cs` |
| Read query | `Identity/…/Features/Users/GetProfile/GetProfileHandler.cs` |
| Security-sensitive write (tracked + audited) | `Identity/…/Features/Auth/RefreshToken/RefreshTokenHandler.cs` |
| Endpoint / validator / command records | the sibling files in `RegisterUser/` |
| **Cross-module event reaction** | `Billing/…/Messaging/ProvisionCreditAccountHandler.cs` |
| Money mutation | use the **adding-billing-command** skill |

## The 4 slice files
1. `{Action}Command.cs` — `sealed record {Action}Command(...) : ICommand<{Action}Response>;` + `{Action}Response` + wire `{Action}Request`.
2. `{Action}Validator.cs` — `AbstractValidator<{Action}Command>`; every rule `.WithErrorCode("dotted.code")`.
3. `{Action}Handler.cs` — the ONLY business logic:
   - **Write + event:** inject `IDbContextOutbox<{Mod}DbContext>`; work on `.DbContext`; `await outbox.PublishAsync(@event)`; `await outbox.SaveChangesAndFlushMessagesAsync()` (**this IS the commit — never also open/commit a transaction**).
   - **Write, no event:** inject the scoped `{Mod}DbContext`; `await db.SaveChangesAsync(ct)`.
   - **Read:** inject `IReadDbContextFactory<{Mod}DbContext>`; `await using var db = f.Create()`; project to the DTO.
   - Throw `ModularPlatformException` subclasses (`ConflictException`, `NotFoundException`, `BusinessRuleException`…) — never build HTTP responses.
4. `{Action}Endpoint.cs` — Minimal API extension method: map Request→Command, `dispatcher.Send/Query`, wrap `ApiResponse<T>.Ok(...)`. No logic, no error handling. `.RequireAuthorization()` unless public.

## Reuse, don't duplicate (DRY)
- **Chain commands for dedup:** a handler may `dispatcher.Send(otherCommand)` to reuse logic (canonical: the Billing/Notifications shells dispatch `EnsureCreditAccount`/`SendNotification`). Don't copy logic between handlers.
- **Concurrency is solved:** xmin + `ConcurrencyRetryBehavior` (auto). For check-and-act under contention use an atomic `ExecuteUpdate` guard. Idempotency = a UNIQUE key column + `catch (DbUpdateException ex) when (ex is not DbUpdateConcurrencyException)` → return already-applied. **Never** roll your own lock or retry loop.
- **Errors/i18n, audit, tenant, time** are all building-block concerns — just throw the right exception / mark `ITenantScoped` / inject `IClock`.

## Cross-module event reaction (a handler in another module)
A `public` shell class `Handle({TheirEvent} e, IDispatcher d, CancellationToken ct)` that dispatches an internal
command. Register it in your module's `ConfigureMessaging` via `options.Discovery.IncludeType<TheHandler>()`.
(Handlers MUST be public; every type in the signature public — see CLAUDE.md §9b Wolverine rules.)

**Idempotent + order-independent (non-negotiable).** The Worker runs handlers in parallel, with NO global ordering,
and may retry/redeliver. Write every handler so running it twice or out of order yields the same result — refetch live
state, guard with a UNIQUE idempotency key (catch `DbUpdateException`), never trust event arrival order. Multiple
handlers for one event currently run sequentially in one transaction (Wolverine default `combined`;
`MultipleHandlerBehavior.Separated` is the parked alternative — CLAUDE.md §9b + backlog §7).

## Wire it
- Add the endpoint to the module's `IModule.MapEndpoints`; the event handler to `ConfigureMessaging`.
- Add new errorCodes to `ModularPlatform.Web/Localization/SharedResource.resx` AND `.cs.resx` (en + cs).

## NEVER
`FooService` · MediatR · `Include`/navigation (by Id only) · **raw SQL** (EF/LINQ only) · validation or error-handling
in endpoints · fire-and-forget publish · a hand-rolled lock/retry · duplicating a building-block.

## Verify
`dotnet build` (0/0, warnings-as-errors) → `dotnet test` → ArchUnitNET green.
