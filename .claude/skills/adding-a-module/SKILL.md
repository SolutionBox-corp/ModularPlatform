---
name: adding-a-module
description: Scaffold a new ModularPlatform module (Core + Contracts + Tests trio) wired via IModule. Use when adding a new product capability area (Billing, Notifications, a product module). Enforces module boundaries + reuse of the shared platform.
---

# Adding a ModularPlatform module

**Read `CLAUDE.md` §1, §3, §5 first.** A module = a trio; its Core is `internal`; it talks to other modules ONLY
via `*.Contracts` integration events. **Copy the Identity module verbatim as the template — don't invent layout.**

## 1. Create the trio
```bash
dotnet new classlib -n ModularPlatform.{Name} -o src/modules/{Name}/ModularPlatform.{Name}
dotnet new classlib -n ModularPlatform.{Name}.Contracts -o src/modules/{Name}/ModularPlatform.{Name}.Contracts
dotnet new xunit    -n ModularPlatform.{Name}.Tests -o src/modules/{Name}/ModularPlatform.{Name}.Tests
```
**CPM gotcha:** the templates emit `<PackageReference … Version="x"/>` and a versioned test SDK — central package
management forbids inline versions. STRIP every `Version=` (versions live in `Directory.Packages.props`; add there
if missing). Mirror Identity's csproj exactly.
References: `.{Name}` → Cqrs, Abstractions, Persistence, Messaging, Web, `.{Name}.Contracts` (+ `Microsoft.EntityFrameworkCore.Design`).
`.{Name}.Contracts` → **Cqrs only** (events implement `IIntegrationEvent`). Add all three to `ModularPlatform.slnx`.

## 2. Core is internal
Everything in `.{Name}` is `internal` EXCEPT the `IModule` impl AND any Wolverine message handler (handlers must be
`public` with public-only `Handle` signatures). Public surface for OTHER modules = `.{Name}.Contracts` only.

## 3. Entities + DbContext (reuse the base)
- Entities derive `Entity`/`AuditableEntity`; mark `ITenantScoped`/`ISoftDeletable`; add `IEntityTypeConfiguration<T>` (snake_case table). **No navigation — by Id only.** Copy `Entities/User.cs`. (xmin, audit, tenant filter come FREE from `PlatformDbContext` — don't re-add them.)
- `{Name}DbContext : PlatformDbContext`, override `ModuleName`, ctor `(DbContextOptions<{Name}DbContext>, ITenantContext)`. Copy `IdentityDbContext.cs`.
- Add `IDesignTimeDbContextFactory<{Name}DbContext>` (copy `IdentityDbContextDesignTimeFactory`).

## 4. IModule (copy `IdentityModule.cs`)
- `RegisterServices`: `AddCqrs(asm)` · `AddValidatorsFromAssembly(asm, includeInternalTypes:true)` · `AddModuleDbContext<T>(Name, writeConn)` (Messaging building-block, Wolverine EF outbox) · `AddModuleReadDbContext<T>(readConn)` · module services. **If the module holds PII:** `AddScoped<IExportPersonalData, …>()` AND `AddScoped<IErasePersonalData, …>()`.
- `MapEndpoints`: call each endpoint extension.
- `ConfigureMessaging`: **register every Wolverine handler explicitly** — `options.Discovery.IncludeType<Messaging.{Handler}>();` (conventional cross-assembly discovery is unreliable; see CLAUDE.md §9b).
- `ApplyMigrationsAsync`: migrate the context via a scope (copy Identity).

## 5. Register the module everywhere it must be known
- `ModuleLoader.Discover(...)` in **Api, Worker, MigrationService** `Program.cs`.
- `LoadAssemblies(...)` in `ArchitectureTests`.
- `"Modules": { "{Name}": { "Enabled": true } }` in each host's appsettings.

## 6. Migration + tests (reuse the shared harness)
`dotnet ef migrations add Initial{Name} --project … --context {Name}DbContext --output-dir Persistence/Migrations`.
Tests: reference `tests/ModularPlatform.IntegrationTesting` (the shared `PlatformApiFactory`) — **do NOT write a new
Testcontainers fixture.** See the **writing-modularplatform-tests** skill. Add the module to the ArchUnit assemblies.

## NEVER
Reference another module's Core · cross-module JOIN · two modules in one DbContext · skip the design-time factory ·
internal Wolverine handlers · leave `ConfigureMessaging` empty when the module has handlers · inline package versions.

## Verify
`dotnet build` 0/0 · `dotnet test` (incl. ArchUnitNET boundary rules) green.
