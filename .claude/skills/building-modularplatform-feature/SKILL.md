---
name: building-modularplatform-feature
description: Scaffold a CQRS vertical slice (command or query) in a ModularPlatform module. Use when adding an endpoint, command, query, handler, or feature. Enforces the frozen pattern — never invent a new flow.
---

# Building a ModularPlatform feature

**Read `CLAUDE.md` §0–§4 first.** A feature is a vertical slice — never a "service".

## Decide: command or query?
- **Mutates state** → `ICommand<T>`. Runs validation + idempotency + transaction + outbox + concurrency-retry.
- **Reads only** → `IQuery<T>`. No transaction, no publish, no mutation.

## Copy the canonical slice
- Write command: `src/modules/Identity/…/Features/Users/RegisterUser/*`
- Read query: `src/modules/Identity/…/Features/Users/GetProfile/*`
- Security-sensitive write: `…/Features/Auth/RefreshToken/RefreshTokenHandler.cs`

## Create `Features/{Feature}/{Action}/` with 4 files
1. `{Action}Command.cs` — `sealed record {Action}Command(...) : ICommand<{Action}Response>;` + `{Action}Response` + wire `{Action}Request`.
2. `{Action}Validator.cs` — `AbstractValidator<{Action}Command>`, every rule `.WithErrorCode("dotted.code")`.
3. `{Action}Handler.cs` — the ONLY business logic:
   - **Write that publishes an event:** inject `IDbContextOutbox<{Mod}DbContext>`; work on `.DbContext`;
     `await outbox.PublishAsync(@event)`; `await outbox.SaveChangesAndFlushMessagesAsync()`.
   - **Write, no event:** inject the scoped `{Mod}DbContext`; `await db.SaveChangesAsync(ct)`.
   - **Read:** inject `IReadDbContextFactory<{Mod}DbContext>`; `await using var db = f.Create()`; project to the response DTO.
   - Throw `ModularPlatformException` subclasses for errors (`ConflictException("code","msg")` …). Never build HTTP responses.
4. `{Action}Endpoint.cs` — Minimal API extension method: map Request→Command, `dispatcher.Send/Query`, wrap `ApiResponse<T>.Ok(...)`. No logic, no error handling. Add `.RequireAuthorization()` unless public.

## Wire it
- Add the endpoint call to the module's `IModule.MapEndpoints`.
- Add new errorCodes to `src/building-blocks/ModularPlatform.Web/Localization/SharedResource.resx` AND `.cs.resx`.

## Never
- No `FooService`. No MediatR. No `Include`/navigation — by Id only. No validation/error-handling in endpoints.
- No fire-and-forget publish. No optimistic concurrency on a money debit path (pessimistic lock — see billing skill).

## Verify
`dotnet build` (0/0) → `dotnet test` → confirm ArchUnitNET green.
