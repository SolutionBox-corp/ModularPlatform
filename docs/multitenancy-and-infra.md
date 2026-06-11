# Multi-tenancy (subdomain-per-tenant) + Infrastructure — Design + Status

> **Stav k 2026-06-11:** část backendu už **POSTAVENA** (Tenancy modul, entitlements guard `.RequireModule`,
> tenant-resolution middleware, platform-admin plane, Payments building-block, per-tenant payment config +
> `ISecretProtector`). Detailní stav položek viz **§8** (tabulka POSTAVENO/SEAM/NEPOSTAVENO + soubory). Infra
> (§6 Caddy/Pulumi) a registrace-join (§5) zůstávají **design**. Plateby viz `payments-multitenant.md`, secrets
> `tenant-secrets.md`.
>
> **Evoluce:** z „B2C per-user" na **B2B subdoména-per-tenant SaaS**. Tenant = **zákazník** (org s více uživateli) na
> `{tenant}.nasedomena.cz`; **platform-admin** (super-admin) na `admin.nasedomena.cz` provisionuje tenanty a zapíná jim
> jednotlivé moduly; `nasedomena.cz` = landing. **Per-user RLS zůstává UVNITŘ tenanta** (uživatelé jednoho zákazníka).
> **Princip: shared TEĎ (pool, jeden deployment + RLS), navrženo pro SILO později (dedikovaný stack/DB per tenant) —
> bez redesignu, jen datový přepnutí.** Vychází z 2026 research (Caddy/Pulumi/AWS pool-bridge-silo/Next.js subdomain).

---

## 1. Tři třídy hostů (jedna codebase, host-based routing)
| Host | Co to je | Auth plane |
|---|---|---|
| `nasedomena.cz` / `www.` | **Landing** (marketing, veřejné) | žádný tenant |
| `admin.nasedomena.cz` | **Platform-admin** — provisionuje tenanty, toggluje per-tenant moduly, plány/limity, vidí celou flotilu | **SYSTEM** (cross-tenant, `IsSystem`), dedikovaná `platform.*` permission, NIKDY pod `{tenant}.` |
| `{tenant}.nasedomena.cz` | **Tenant app** — uživatelé zákazníka, RLS-scoped, nav řízená entitlements | tenant-scoped (JWT `tenant_id`); tenant-admin = role `admin` UVNITŘ tenanta |

`admin` a apex jsou **rezervované labely** (reserved-slug list) — nikdy nejsou tenant.

---

## 2. Tenant registry = JEDINÝ zdroj pravdy (Postgres, ne IaC)
Platform-owned tabulka `tenants` (+ `tenant_entitlements`). **DB je desired state; IaC/compose je jen actuator** (nikdy
ať Pulumi stack / .tf NEJSOU zdroj pravdy — jsou odvozené artefakty).
```
tenants:           Id · Subdomain(unique) · Name · Status(provisioning|active|suspended|separating|dedicated)
                   · Placement(shared | dedicated:<connKey>) · DbDsnSecretRef? · InfraRevision · CreatedAt
tenant_entitlements: TenantId · ModuleKey · Enabled · Tier/Plan · Limits(JSONB) · ValidFrom/To
custom_domains?:   (Phase 3) TenantId · Domain · Verified   ← pro vanity domény
```

---

## 3. Per-tenant module entitlements (deployment flag → per-tenant data)
- **Deployment `Modules:{Name}:Enabled` = OUTER bound** (je kód modulu vůbec nahraný v tomto stacku — host pořád
  načítá všechny module assemblies při startu, beze změny).
- **`tenant_entitlements` = INNER grant** (má TENTO zákazník modul zapnutý). Platform-admin toggle = 1 UPDATE, účinný
  **další request** (žádný re-login, NE v JWT — bylo by stale).
- **Enforcement = `ModuleEntitlementGuard`** (CQRS pipeline behavior / endpoint filter po auth): každý modul deklaruje
  `module_key` přes `.RequireModule("billing")` (mirror `.RequirePermission`). Tenant nemá entitlement → **404**
  (route-not-found shape, **NE 403** — neleakovat existenci) přes nový `NotEntitledException`→404. RLS chrání DATA,
  guard chrání FEATURE/endpoint.
- **`GET /v1/tenant/me/entitlements`** → `{ tenant, tier, modules:[{key,enabled}], limits }` = **JEDINÝ zdroj pro FE
  nav** (advisory UX; guard je skutečné vynucení — skrytý nav item, jehož endpoint se přesto zavolá, MUSÍ 404).

---

## 4. Isolation: POOL teď, SILO později (AWS pool/bridge/silo)
- **Teď = pool:** jeden .NET deployment, sdílený Postgres + RLS (`app.principal_id` GUC, `app_rls` role) — **už hotové**.
  Všichni `placement='shared'`.
- **Klíčový seam (postav od dne 1):** **connection string se čte z resolved tenant `Placement`, NE z globální
  `ConnectionStrings:Write/:Read`** konstanty. `placement='shared'` → vrátí sdílené stringy (identické s dneškem).
- **Silo (split tenanta X):** provisionuj dedikovaný Postgres → migrate X rows (pg_dump/restore nebo logical
  replication pro low-downtime) → `MigrationService` proti němu → flip `Placement='dedicated:tenantX-conn'` → requesty
  X transparentně jdou na dedikovanou DB. **Žádný redesign — datový přepnutí na již existujícím hot-path seamu.**

---

## 5. Tenant resolution (Host → tenant, cross-check s tokenem)
- **Middleware** mapuje `Host` (label `{tenant}`) → `Tenant` record **brzy**, vybere `Placement` (→ connection) a
  **cross-checkne s JWT `tenant_id`** (musí sedět, jinak 401/404 — token pro tenanta A na subdoméně B = odmítnuto,
  defense-in-depth). **Identita pořád z tokenu** (Law 10); subdoména = routing/placement klíč + cross-check.
- apex/admin → **žádný tenant** (SYSTEM/landing). Neznámý/suspended subdomain → 404/landing.
- **Registrace se mění:** na `{tenant}.` register/invite **vstupuje do EXISTUJÍCÍHO tenanta** (subdomény), NEvytváří
  nový (to byl starý B2C flow). Tenanty zakládá platform-admin. (Self-serve signup = volitelně apex „create workspace"
  → provisionuje tenant + subdoménu.)

---

## 6. Infrastruktura

### Phase 1 — shared (docker-compose + Caddy), provisioning = čistý DB insert
- **Stack (jeden `docker-compose`):** `caddy` · `web` (Next.js) · `api` (.NET) · `worker` · `jobs` · `migrator` ·
  `postgres` · `redis`.
- **Caddy 2 edge** (custom image přes **xcaddy** s `caddy-dns/cloudflare`): **JEDEN wildcard `*.nasedomena.cz` cert
  přes ACME DNS-01** (wildcard VYŽADUJE DNS-01; HTTP-01 neumí). **Pokryje VŠECHNY subdomény → žádný per-tenant cert,
  žádný Let's Encrypt rate-limit** (50 cert/doména/7 dní). Explicitní host bloky (most-specific first): apex (landing) ·
  `admin.` (admin app) · pak `*.nasedomena.cz` (tenant → web). Apex přidat do certu / druhý cert pro bare apex.
- **Provisioning tenanta = INSERT `tenants` row.** Wildcard DNS (`*.nasedomena.cz` → edge IP, jednou) + wildcard cert +
  `*.` Caddy blok ⇒ nová subdoména **resolvuje a servíruje TLS okamžitě**, proxy nepotřebuje NIC. App gatuje neznámé
  tenanty (404). **❌ ZAHOĎ „write nginx config + cron reload"** — racy, drift-prone, řeší problém, který wildcard
  odstraňuje (research-potvrzený anti-pattern).
- **On-demand TLS + `ask` endpoint** (`GET /internal/tls-allowed?domain=` → 200 jen pro verified custom doménu)
  **rezervováno JEN pro Phase 3 vanity domény** (`portal.acme.com` → tenant), NE pro `*.nasedomena.cz`.

### Phase 2 — separation (Pulumi Automation API, reconciler)
- **Provisioning worker** (samostatný container, base `pulumi/pulumi` image = CLI přítomné) běží **idempotentní
  reconcile loop** (Wolverine/hosted background, **NE Quartz cron**, **NE v HTTP requestu**): čte `tenants` kde desired
  ≠ actual, konverguje, update `InfraRevision`, backoff. Trigger na změnu registry + pomalý drift-sweep (Quartz jen
  spouští sweep).
- **Pulumi Automation API (.NET SDK, inline programs, jeden stack `tenant-<id>` per dedikovaný tenant)** — Pulumi >
  Terraform tady: embeddable strongly-typed C# SDK volatelný přímo z provisioning service (loops/conditionals), Terraform
  nemá ekvivalent (shell-out CLI + HCL templating). Stack stojí dedikovanou DB + app + DNS + routing, runne
  `MigrationService`, zkopíruje tenant rows, cutover, flip `Status`.
- **Pulumi state = DIY backend** na S3-compat (MinIO/R2/S3, `pulumi login s3://…`), **passphrase secrets provider** per
  stack (AES-256-GCM), v registry jen secret **reference**, ne hodnota. Žádný Pulumi Cloud, žádný lock-in.
- **Dlouhoběžící provision/separate** = expose přes existující **Operations 202/status** pattern (caller pollује).
- **Caddy routing dedikovaného tenanta** = `Placement` nese upstream; per-host route (Caddy admin API `POST /config/…`
  nebo dedikovaný stack běží vlastní Caddy se sdíleným wildcard certem) → `{tenantX}.` jde na dedikovaný stack, ostatní
  zůstávají pooled. **Additivní, reverzibilní, žádná DNS/cert změna.**

---

## 7. Frontend (Next.js, jedna app — viz handoff §tenancy)
- **`proxy.ts`** (Next 16 rename `middleware.ts`) host-based rewrite → route groups `(marketing)`/`(admin)`/`(tenant)`.
  Resolve host → 3 větve (apex/www → marketing; `admin.` → admin; `*.ROOT` → tenant). Injektuj **server-trusted
  `x-tenant`** header (strip inbound client `x-tenant` — anti-spoof); tenant identita SOLELY ze subdomény, nikdy z
  path/body (IDOR parita s backendem).
- **Host-only session cookie** (httpOnly Secure SameSite=Lax, **BEZ `Domain` atributu** → bound na přesnou subdoménu;
  **NIKDY `Domain=.nasedomena.cz`** = cross-tenant token bleed). Cookie doména runtime per request → **ne NextAuth**.
- **Entitlement-driven nav:** BFF po loginu 1× `GET /v1/tenant/me/entitlements` → TanStack key `['entitlements',tenant]`
  → řídí nav + route guardy. FE **nikdy nehardcoduje** seznam modulů.
- **Dev hosty:** `*.lvh.me` (DNS→127.0.0.1) / `*.localhost`; `ROOT_DOMAIN=lvh.me:3000`; mkcert pro lokální HTTPS.

---

## 8. Co se mění na BACKENDU — STAV (částečně POSTAVENO 2026-06-11)

> **Status k 2026-06-11:** §8.1 (Tenancy modul + entitlements), §8.3 (guard + `.RequireModule`), §8.4 (tenant-resolution
> middleware), §8.6 (platform-admin plane) jsou **POSTAVENÉ a otestované**. K nim přibyl **provider-agnostic Payments
> building-block** + per-tenant payment config + `ISecretProtector` (nebyly v původním §8 designu, viz
> `payments-multitenant.md` + `tenant-secrets.md`). §8.2 (placement-driven connection) je **seam připravený, dnes no-op
> shared**; §8.5 (registrace→join) a §8.7 (Phase-2 provisioning) **zatím NEpostaveno**. Billing→multi-tenant
> (webhook/saga/reconcile per tenant) je **rozpracované** (FÁZE 2D-ii-b).

| # | Položka | Stav | Soubor(y) |
|---|---|---|---|
| 1 | **Tenancy/Provisioning capability** — `tenants` + `tenant_entitlements`, platform-admin commands (provision/entitle), SYSTEM, `platform.*` | ✅ **POSTAVENO** (Suspend/Separate odloženo) | `src/modules/Tenancy/…/Entities/{Tenant,TenantEntitlement}.cs`, `…/Services/{TenantDirectory,EntitlementResolver,TenantProvisioning}.cs`, `…/Features/Admin/{ProvisionTenant,SetEntitlement}/`, migrace `20260611083522_InitialTenancy` |
| 2 | **Placement-driven connection resolution** (pool→silo seam) | 🟡 **SEAM, dnes no-op shared** (`ITenantPlacementResolver` odložen k 1. dedikovanému tenantovi) | connection wiring `Messaging/DependencyInjection.cs` + `Persistence/ReadDbContextFactory.cs` |
| 3 | **`ModuleEntitlementGuard`** + `.RequireModule(key)` + `NotEntitledException`→404 + `GET /v1/tenant/me/entitlements` | ✅ **POSTAVENO** | `src/building-blocks/ModularPlatform.Web/Authorization/EndpointAuthorization.cs` (`RequireModule` = `IEndpointFilter`, live `IEntitlementResolver` lookup), `Cqrs/Errors.cs` (`NotEntitledException`), `…/Tenancy/…/Features/Entitlements/GetMyEntitlements/` |
| 4 | **Tenant-resolution middleware** (Host→tenant, cross-check JWT `tenant_id`) | ✅ **POSTAVENO** | `src/building-blocks/ModularPlatform.Web/TenantResolutionMiddleware.cs` (PO `UseAuthentication`, PŘED `UseRateLimiter`; mismatch→401 `auth.tenant_mismatch`, unknown/suspended→404) |
| 5 | **Registrace → join existujícího tenanta** (ne create-new) | ⏳ **NEPOSTAVENO** (registrace dnes pořád provisionuje tenant per user) | — |
| 6 | **Platform-admin plane** (cross-tenant SYSTEM, `platform.*`, jen `admin.`) | ✅ **POSTAVENO** | `PlatformPermissions.PlatformTenantsManage = "platform.tenants.manage"`, slices `ProvisionTenant`/`SetEntitlement` gated `.RequirePermission(platform.tenants.manage)` |
| 7 | **(Phase 2)** Provisioning worker + reconciler + Pulumi seam; `/internal/tls-allowed` | ⏳ **NEPOSTAVENO** (až 1. dedikovaný tenant) | — |
| + | **Payments building-block** (`IPaymentGateway` + Stripe + GoPay + Fake + `IPaymentGatewayResolver(tenant,plane)`) — NOVÉ proti orig. §8 | ✅ **POSTAVENO** | `src/building-blocks/ModularPlatform.Payments/{IPaymentGateway,PaymentResolution,StripePaymentGateway,GoPayPaymentGateway,FakePaymentGateway}.cs` — viz `payments-multitenant.md` |
| + | **Per-tenant payment config + `ISecretProtector`** (`PaymentConfiguration`+`tenant_secrets`, self-service `ConfigureGateway`) — NOVÉ | ✅ **POSTAVENO** (Billing webhook/saga/reconcile→resolver rozpracováno) | `src/building-blocks/ModularPlatform.Secrets/`, `src/modules/Billing/…/Entities/PaymentGatewayEntities.cs`, `…/Payments/BillingPaymentConfigStore.cs`, `…/Features/PaymentGateway/ConfigureGateway/` — viz `tenant-secrets.md` |

> Pořadí: §8.1/§8.3/§8.4/§8.6 + Payments/Secrets (shared) **hotovo** → Billing→multi-tenant webhook/saga/reconcile per
> tenant (rozpracováno) → registrace-join + frontend 3-host → Phase-2 provisioning až bude první dedikovaný tenant.
> Per-user RLS + tenant filter + ITenantContext = **už hotové**, tenant-as-customer se nad ně vrství.

---

## 9. Anti-patterns (research-potvrzené — NEDĚLAT)
- ❌ nginx config write + cron reload pro subdomény (racy, drift) → Caddy wildcard DNS-01.
- ❌ on-demand per-subdomain cert pro `*.nasedomena.cz` (Let's Encrypt 50/týden limit) → jeden wildcard.
- ❌ Pulumi/.tf jako zdroj pravdy o tenantech → registry v Postgres je autoritativní.
- ❌ `pulumi up` synchronně v admin HTTP requestu → provisioning worker + 202/status.
- ❌ modul list / plán / limity v JWT (stale) → per-request z `tenant_entitlements`.
- ❌ tři Next.js appky → jedna app, host-based route groups (sdílený BFF/Query/SSE).
- ❌ `Domain=.nasedomena.cz` cookie → host-only (per-subdomain).
- ❌ Pulumi pro shared phase / silo infra teď → overengineering; docker-compose, Pulumi až u prvního dedikovaného.
- ❌ trust client-supplied tenant id (header/path/body) → SOLELY z validované subdomény + token cross-check.
