---
name: writing-modularplatform-tests
description: Write tests for a ModularPlatform feature or module — slice/integration tests on Testcontainers-Postgres plus ArchUnitNET boundary rules. Use right after implementing a feature or module.
---

# Writing ModularPlatform tests

**Read `CLAUDE.md` §9 first.** Test the behavior through the real seams (dispatcher, DbContext, outbox), not the
internals.

## Three layers
1. **Architecture (ArchUnitNET)** — in `tests/ModularPlatform.ArchitectureTests`. When you add a module, add its
   assemblies to `LoadAssemblies(...)`. The existing rules then automatically assert:
   - `*.Contracts` depends on no infrastructure.
   - a module's Core depends on no other module's Core.
   These must stay green — they are the boundary law.

2. **Slice / integration** — per module `*.Tests`, on **Testcontainers-Postgres** (real DB, real migrations,
   real interceptors). Pattern:
   - spin a `PostgreSqlContainer`, point `ConnectionStrings:Write/Read` at it, run `ApplyMigrationsAsync`.
   - resolve `IDispatcher`; `Send`/`Query` real commands; assert on the response AND on DB state.
   - **Must-cover behaviors:**
     - audit row contains ONLY changed fields (JSONB) after an update;
     - duplicate integration event / Stripe id is processed once (idempotency);
     - concurrent debit never goes below zero (no double-spend) — for billing;
     - refresh-token reuse revokes the whole family — for auth.

3. **Validation** — a validator unit test asserting the right `errorCode` for bad input (no DB needed).

## Conventions
- xUnit + `Shouldly`. One behavior per test. Arrange via real commands, not by poking the DB directly where a
  command exists.
- Never mock the DbContext — use Testcontainers. Never assert on private state — assert through queries.

## Verify
`dotnet test` green, including the ArchUnitNET project.
