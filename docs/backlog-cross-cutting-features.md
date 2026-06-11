# Backlog — cross-cutting features (návrhy k pozdější implementaci)

> **Účel:** zaparkované návrhy pěti cross-cutting schopností, na které se navazuje **později, jiným agentem/session**.
> Vzniklo 2026-06-11 z architektonické diskuse. **Není to schválený plán k okamžité implementaci** — je to
> design + rozhodovací body. Než se cokoliv staví: přečíst `CLAUDE.md` (§4 co je už vyřešené, §10 NOT YET) a počkat,
> až doběhne běžící money-sensitive **Fáze 2** (Billing→multi-tenant) z `~/.claude/plans/po-dek-v-docs-merry-donut.md`,
> jinak hrozí kolize v `src/modules/Billing` + `src/hosts`.
>
> **Pravidlo:** každá z těchto věcí je „NOT YET" → před stavbou potvrď návrh s userem (Law 11). Battle-tested lib
> před vlastní implementací. Vše musí ctít boundary law (jen přes `*.Contracts`, žádný cross-module JOIN, eventuální
> konzistence).

---

## Kontext — co už NENÍ potřeba řešit (hotovo/in-flight)

Aby budoucí agent neduplikoval: **multitenancy, subdoména-per-tenant, payment provider port (Stripe+GoPay),
secret-at-rest seam (KEK/KMS)** jsou hotové nebo rozpracované pod schváleným programem (200+ testů). Viz
`[[multitenancy-payments-program]]` (memory) + `docs/multitenancy-and-infra.md`. **KEK/KMS:** `ISecretProtector`
(`Secrets:Provider = local|kms`) už existuje s `LocalMasterKeySecretProtector`; přechod na KMS = doimplementovat KMS
providera se stejným envelope tvarem (DEK zabalený KEKem, KEK nikdy neopustí KMS). Není to v tomto backlogu.

---

## 1. Cross-module saga vzor s kompenzací (rozšíření existujícího)

**Proč:** `CreditPurchaseSaga` je kanonická saga, ale jen **uvnitř Billingu**. Chybí vzor pro **opravdu cross-module**
workflow s rollbackem (např. „zřízení tenanta" přes Tenancy + Billing + Notifications, kde když platba selže, vrátí se
i předchozí kroky).

**Očekávání (upřímně):** NE distribuovaná ACID transakce / 2PC. Co dostaneš = durable, idempotentní, self-healing
workflow. „100 %" garance stojí na 5 pravidlech:
1. Stav sagy perzistovaný DŘÍV než jakýkoli side-effect (EF-persisted v module DbContextu).
2. Každý side-effect idempotentní (UNIQUE idempotency key) → dvojí doručení nezdvojí efekt.
3. Řízeno outboxovanými zprávami + inbox dedup (exactly-once).
4. Timeout = „nestalo se"; `NotFound` = „přišlo pozdě po expiraci sagy".
5. **Kompenzace jako explicitní stavy** — krok N selže → saga pošle kompenzační command rušící krok N-1 (NE přes výjimky).

**Návrh deliverables:**
- Nová kanonická cross-module saga (kandidát: `TenantProvisioningSaga` — Tenancy provision → Billing payment-config →
  Notifications welcome; rollback při selhání).
- Skill/checklist `writing-a-saga` (kdy event vs saga, jak definovat kompenzace, jak testovat partial-failure).
- Reconciliation job jako backstop (per existující vzor `ReconcileStripeCommand`).

**Rozhodnutí:** který konkrétní business flow je první cross-module saga (musí mít reálné kompenzovatelné kroky).

---

## 2. Cross-module dotazy — read-model / sklad (3 úrovně, nasazovat dle bolesti)

**Proč:** JOINy přes moduly nebudou (boundary law). Potřeba „optimalizovat když potřeba", ne preventivně.

| Úroveň | Co | Kdy | Cena |
|---|---|---|---|
| **A) API composition** | dotaz každého modulu zvlášť + sešití v kódu | default, malé N, per stránka | 0 infra |
| **B) Materializovaný read-model** | denormalizovaná projekce plněná **event handlery** z více modulů (vlastní tabulka) | konkrétní pomalý dotaz | eventuální konzistence |
| **C) Analytický sklad (OLAP)** | eventy/CDC → ClickHouse / read replica / DuckDB | BI, těžké reporty | offline, nikdy v request path |

**Klíč:** read-model (B) se staví **z eventů**, ne JOINem zdrojů → boundary law drží, jede přes stejný outbox/inbox.

**Návrh deliverables:**
- Seam — modul `Reporting`/`ReadModels`, který vlastní projekce (subscribuje cizí integration eventy, drží denormalizované tabulky).
- Kanonický příklad jedné projekce (např. „tenant overview": počet userů + balance + poslední platba) složené z eventů Identity/Billing/Tenancy.
- Dokumentovat „upgrade path" A → B → C, aby se sahalo po B/C jen na hot dotazy.

**Rozhodnutí:** první hot dotaz, který composici (A) nestíhá; volba OLAP enginu (až úroveň C).

---

## 3. Feature flags (≠ entitlements!)

**Důležité rozlišení:** `IEntitlementResolver` (už existuje) = **komerční** brána („koupil si tenant modul X?").
Feature flags = **provozní** přepínače chování (postupný rollout, A/B, kill-switch, per-tenant/per-user/procento).
Jsou to dvě různé věci, nemíchat.

**Návrh (battle-tested first):**
- Port `IFeatureFlags.IsEnabledAsync(key, context)` nad **OpenFeature** (CNCF standard) → swap providera bez změny call-sitů.
- Start: jednoduchý DB-backed provider (per-tenant/global, cache, invalidace eventem). Později flagd / GrowthBook / atd.
- Vyhodnocení v handleru nebo na edge (endpoint filter). Kontext = tenant + user z `ITenantContext`.

**Rozhodnutí:** OpenFeature jako abstrakce ano/ne; scope flagů (global / per-tenant / per-user / %); kde se spravují (admin UI vs config).

---

## 4. Bulk operace (vzor — bude se dít často)

**Dvě varianty podle velikosti, obě reuse existující stavební kameny:**
- **Malý synchronní bulk (≤ ~100 položek):** jeden `BulkXCommand(items[])` → handler zvaliduje **všechny**, aplikuje
  batchově (`AddRange` / `ExecuteUpdate`), vrátí **per-item výsledek** (success/chyba). Jedna transakce v rámci modulu,
  idempotence per item přes UNIQUE key. (Pozor: `ExecuteUpdate`/`ExecuteDelete` obchází audit + xmin — viz CLAUDE.md §4.)
- **Velký async bulk (tisíce):** accept → **Operation** (202 + status, modul `Operations` už existuje) → worker zpracuje
  **po dávkách**, reportuje progress, partial-failure report ke stažení. Durable přes outbox.

**Návrh deliverables:** skill `bulk-operations` (kanonický vzor obou variant) + jeden referenční bulk endpoint.

**Rozhodnutí:** práh kdy sync→async (default ~100); formát partial-failure reportu.

---

## 5. Fulltext search

**Návrh (battle-tested, no-new-infra first):**
- Start: Postgres `tsvector` + `pg_trgm` (GIN index) — žádná nová infra, per-modul (každý modul indexuje svá data, boundary drží).
- Seam `ISearchIndex` (index/query port), aby se dalo později přejít na ElasticSearch / Meilisearch / Typesense, až to přeroste Postgres.
- Cross-module search = composice výsledků per-modul (ne globální index nad cizími tabulkami), nebo dedikovaný search read-model (viz bod 2-B).

**Rozhodnutí:** rozsah (per-modul vs global), jazyk/stemming (cs+en), kdy opustit Postgres FTS.

---

## 6. (drobnost) HELLO1 / HELLO2 subdoménové demo

`TenantResolutionMiddleware` už řeší `hello1.localhost` → tenant1 atd. Stačí endpoint `GET /` vracející
`HELLO {tenant.Name}` pro vizuální důkaz multitenancy. **Triviální, ale dotýká se `Api/Program.cs`** (rozpracováno Fází 2)
→ udělat koordinovaně / ve worktree, ať nekoliduje. Volitelně až s FE foundation (Fáze 4).

---

## 7. Messaging — `MultipleHandlerBehavior.Separated` (zvážit)

**Stav dnes:** `MultipleHandlerBehavior` není nastaven → Wolverine default `combined`: víc handlerů na JEDEN event běží
**sekvenčně v jedné transakci/envelope** (např. `UserRegisteredIntegrationEvent` → Billing `ProvisionCreditAccount` +
Notifications `SendWelcome` v jedné obsluze, ne paralelně). Výjimka retryuje CELOU envelope (oba handlery znovu) —
bezpečné jen díky tomu, že každý handler je idempotentní.

**Návrh:** pro modulární monolit Wolverine doporučuje `options.MultipleHandlerBehavior = MultipleHandlerBehavior.Separated`
— každý handler „sticky" na vlastní local queue → nezávislý (vlastní retry/DLQ, může běžet paralelně), selhání jednoho
modulu neretryuje druhý. POZOR: mění idempotency semantiku → zvážit `MessageIdentity.IdAndDestination` (jinak by se
dva handlery jednoho eventu hádaly na inbox dedup) + vznikne víc local queues.

**Rozhodnutí:** přepnout na `Separated` až bude víc než pár odběratelů na jeden event, nebo až začne vadit spřažení
retry napříč moduly. Do té doby `combined` + idempotentní handlery stačí.

**Pravidlo platné v OBOU režimech (zapsáno i v CLAUDE.md §9b):** globální pořadí napříč zprávami NENÍ garantované
(competing consumers, paralelní, multi-instance) → handlery piš VŽDY **idempotentní + order-independent** (refetch
živého stavu, ne „aplikuj delta v pořadí"). Race conditions ošetřeny vrstvami: xmin + `ConcurrencyRetryBehavior`,
atomický `ExecuteUpdate` guard (peníze), inbox dedup (UNIQUE MessageId), idempotency UNIQUE key. Když fakt potřebuješ
FIFO: sticky sekvenční local queue nebo partition-by-key.

## Doporučené pořadí (návrh, k potvrzení)

1. **Feature flags** + **bulk ops** (nezávislé, vysoká denní užitečnost, nízké riziko).
2. **Cross-module read-model** seam (úroveň B) — až přijde první pomalý cross-module dotaz.
3. **Cross-module saga vzor** — až bude reálný kompenzovatelný flow (např. tenant provisioning).
4. **Fulltext** — až bude konkrétní search požadavek.
5. HELLO demo — kdykoliv koordinovaně / s FE.

Vše AŽ po doběhnutí běžící Fáze 2 (money-sensitive), aby nedošlo ke kolizi.
