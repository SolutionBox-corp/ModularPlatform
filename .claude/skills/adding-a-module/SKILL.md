---
name: adding-a-module
description: Scaffold a new ModularPlatform module (the Core + Contracts + Tests trio) wired via IModule. Use when adding a new product capability area (e.g. Billing, Notifications, a product-specific module). Enforces module boundaries.
---

# Adding a ModularPlatform module

**Read `CLAUDE.md` §1, §3, §5 first.** A module = a trio; its Core is `internal`; it talks to other modules
ONLY via `*.Contracts` integration events. Copy the **Identity** module as the template.

## 1. Create the trio
```bash
dotnet new classlib -n ModularPlatform.{Name} -o src/modules/{Name}/ModularPlatform.{Name}
dotnet new classlib -n ModularPlatform.{Name}.Contracts -o src/modules/{Name}/ModularPlatform.{Name}.Contracts
dotnet new xunit    -n ModularPlatform.{Name}.Tests -o src/modules/{Name}/ModularPlatform.{Name}.Tests
```
References:
- `.{Name}` → Cqrs, Abstractions, Persistence, Messaging, Web, `.{Name}.Contracts` (+ pkg `Microsoft.EntityFrameworkCore.Design`).
- `.{Name}.Contracts` → **Cqrs only** (integration events implement `IIntegrationEvent`).
- Add all to `ModularPlatform.slnx`.

## 2. Make Core internal
Every class/record in `.{Name}` is `internal` except the `IModule` impl. Public surface = `.{Name}.Contracts`.

## 3. Entities + DbContext
- Entities derive `Entity`/`AuditableEntity`; mark `ITenantScoped`/`ISoftDeletable`; add `IEntityTypeConfiguration<T>`
  (snake_case table). **No navigation properties.** (Copy `Entities/User.cs`.)
- `{Name}DbContext : PlatformDbContext`, override `ModuleName`, ctor `(DbContextOptions<T>, ITenantContext)`.
- Add `IDesignTimeDbContextFactory<{Name}DbContext>` (copy `IdentityDbContextDesignTimeFactory`).

## 4. IModule (copy `IdentityModule.cs`)
- `RegisterServices`: `AddCqrs(asm)`, `AddValidatorsFromAssembly(asm, includeInternalTypes:true)`,
  `AddModuleDbContext<T>(Name, writeConn)`, `AddModuleReadDbContext<T>(readConn)`, + module services.
- `MapEndpoints`: call each endpoint extension.
- `ConfigureMessaging`: usually empty (Wolverine auto-discovers handler classes in the assembly).
- `ApplyMigrationsAsync`: migrate the context via a scope.

## 5. Register the module everywhere it must be known
- `ModuleLoader.Discover(...)` in Api, Worker, MigrationService `Program.cs`.
- `LoadAssemblies(...)` in `ArchitectureTests`.
- `"Modules": { "{Name}": { "Enabled": true } }` in appsettings of each host.

## 6. Migration + tests
`dotnet ef migrations add Initial{Name} --project … --context {Name}DbContext --output-dir Persistence/Migrations`
Add a boundary test assertion and at least one slice test (Testcontainers-Postgres).

## Never
Reference another module's Core. JOIN across modules. Put two modules in one DbContext. Skip the design-time factory.

## Verify
`dotnet build` 0/0 · `dotnet test` (incl. ArchUnitNET boundary rules) green.
