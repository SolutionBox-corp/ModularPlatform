# Oblast 00 — Collections & tenancy
Tato oblast pokrývá životní cyklus a vlastnictví **knowledge collections** v modulu `HybridRag` — vytvoření, výpis, přejmenování a mazání kolekcí ve `Scope = Tenant | User`, permission gating (tenant-admin vs běžný user), sémantiku dvouvrstvého vlastnictví korpusu, dvouvrstvou RLS izolaci (`ITenantScoped` tvrdá hranice + custom RLS policy `tenant_id AND (scope = Tenant OR owner_user_id = principal)`), automatický provisioning výchozí tenant kolekce při registraci tenanta a přiřazení/přesun dokumentů mezi kolekcemi. Mapuje se na build fázi **HybridRag F1 — Collections & tenancy foundation** (entity `KnowledgeCollection`, RLS bootstrap, IModule wiring), která je nutným předpokladem pro ingest (Oblast 01) i retrieval (Oblast 02), protože každý `Document`/`Chunk`/`GraphNode` dědí `Scope` a `OwnerUserId` od své kolekce.

## UC-00-01 — Tenant-admin vytvoří tenant-scope kolekci
- **Actor / role:** tenant-admin
- **Precondition:** Autentizovaný uživatel s permission `RagCollectionManageTenant`; existuje aktivní tenant (claim `tenant_id` v tokenu); modul `HybridRag` je entitled (`Modules:HybridRag:Enabled=true` + tenant entitlement).
- **Trigger:** `POST /v1/hybridrag/collections` (body `{ name, scope: "Tenant" }`)
- **Main flow:** Endpoint mapuje `CreateCollectionRequest → CreateCollectionCommand(Scope=Tenant, Name)` → `IDispatcher.Send` → Validation behavior (`CreateCollectionValidator`) → `CreateCollectionHandler`: získá `TenantId` z `ITenantContext` (NE z body), `OwnerUserId = null` (tenant-scope nemá osobního vlastníka), `Id = Guid.CreateVersion7()`, vloží `KnowledgeCollection` přes `IDbContextOutbox<HybridRagDbContext>`, publikuje `CollectionCreatedIntegrationEvent`, `SaveChangesAndFlushMessagesAsync()` (= commit). Endpoint zabalí `ApiResponse<CollectionResponse>` a vrátí 201 + `Location` přes named route + `LinkGenerator`.
- **Postcondition / záruky:** Řádek v `knowledge_collections` (scope=Tenant, owner_user_id=NULL, tenant_id stamped), audit entry v `hybridrag_audit_entries`, publikován `CollectionCreatedIntegrationEvent`. Idempotence přes UNIQUE `(tenant_id, scope, owner_user_id, lower(name))`.
- **Tenancy / permissions:** Scope Tenant; vyžaduje `RagCollectionManageTenant`; `TenantStampingInterceptor` orazí `TenantId`; custom RLS policy povolí tenant-scope řádek všem v tenantu.
- **Reuse / canonical pattern:** `Features/Users/RegisterUser/*` (4-soubor slice) + `RegisterUserHandler.cs:22` (outbox publish), entity config dle `FileObject.cs:15`, endpoint Location dle `StartDemoOperationEndpoint`.
- **Data dotčena:** `knowledge_collections`, `hybridrag_audit_entries` · **Eventy:** `CollectionCreatedIntegrationEvent`
- **Priorita:** P0

### Edge cases UC-00-01
- **EC-00-01-01 — Prázdný / whitespace Name** · Trigger: `name=""` nebo `"   "` · Očekávané chování: 400 ValidationProblem s errorCode `rag.collection.name_required` · Mechanismus: `CreateCollectionValidator` `.NotEmpty().WithErrorCode("rag.collection.name_required")` (zákon §4 Validation) · Severity: P2 · Test: integration POST prázdný name → 400, body obsahuje errorCode.
- **EC-00-01-02 — Name přes max délku** · Trigger: `name` > 200 znaků · Očekávané chování: 400 `rag.collection.name_too_long` · Mechanismus: validator `.MaximumLength(200)`; DB sloupec má odpovídající limit · Severity: P2 · Test: 201 znaků → 400.
- **EC-00-01-03 — Duplicitní název v rámci tenant scope** · Trigger: druhý create se stejným `lower(name)`, `scope=Tenant`, `owner_user_id=NULL` ve stejném tenantu · Očekávané chování: 409 `rag.collection.name_taken`, žádný druhý řádek · Mechanismus: UNIQUE index → handler `catch (DbUpdateException)` přemapuje na `ConflictException` (zákon §2 idempotency = UNIQUE + catch) · Severity: P1 · Test: dvojí create stejné jméno → 2. = 409, count=1.
- **EC-00-01-04 — Case-insensitive kolize ("Sales" vs "sales")** · Trigger: existuje "Sales", create "sales" · Očekávané chování: 409 (považováno za duplikát) · Mechanismus: UNIQUE na `lower(name)` (functional index) · Severity: P2 · Test: 2 varianty case → 409.
- **EC-00-01-05 — Klient pošle `tenantId`/`ownerUserId` v body** · Trigger: body obsahuje cizí `tenantId` · Očekávané chování: pole se IGNORUJE, použije se token (žádný IDOR) · Mechanismus: Command nemá tenantId/ownerUserId z requestu; handler bere `ITenantContext` (zákon §10) · Severity: P0 · Test: pošli cizí tenantId → řádek má tenant_id z tokenu.
- **EC-00-01-06 — Chybí permission `RagCollectionManageTenant`** · Trigger: běžný user volá create scope=Tenant · Očekávané chování: 403 ForbiddenException · Mechanismus: `.RequirePermission(PlatformPermissions.RagCollectionManageTenant)` na endpointu · Severity: P0 · Test: token bez permission → 403.
- **EC-00-01-07 — Chybějící tenant claim (system/anon kontext)** · Trigger: token bez `tenant_id` · Očekávané chování: 401/403, NIKDY se netvoří „globální“ kolekce viditelná všem · Mechanismus: tenancy filtr netraktuje chybějící claim jako „vidět vše“; create v Api background = SYSTEM kontext (zákon: missing tenant ≠ all) · Severity: P0 · Test: token bez tenant_id → nelze vytvořit tenant řádek.
- **EC-00-01-08 — Modul není entitled pro tenant** · Trigger: tenant bez `HybridRag` entitlementu · Očekávané chování: 404 (ModuleEntitlementGuard) · Mechanismus: entitlement guard → 404 (multitenancy doc) · Severity: P1 · Test: entitlement off → 404.
- **EC-00-01-09 — Concurrent create stejného jména (race)** · Trigger: 2 paralelní requesty stejné jméno · Očekávané chování: přesně 1 uspěje (201), druhý 409, žádný duplikát · Mechanismus: UNIQUE index serializuje na DB, oba projdou outboxem, jeden `DbUpdateException` · Severity: P1 · Test: 2-way paralelní → (201,409), count=1.
- **EC-00-01-10 — Neplatný `scope` enum** · Trigger: `scope="Global"` · Očekávané chování: 400 `rag.collection.scope_invalid` · Mechanismus: validator `.IsInEnum()` / explicit allowlist Tenant|User · Severity: P2 · Test: nevalidní scope → 400.
- **EC-00-01-11 — Name s control/zero-width znaky nebo emoji** · Trigger: `name` obsahuje `\u200b`, RTL override, NULL byte · Očekávané chování: trim + odmítnutí control znaků (`rag.collection.name_invalid_chars`) nebo normalizace, žádné homograph spoofing · Mechanismus: validator regex/Unicode normalizace (NFC), strip control · Severity: P2 · Test: zero-width name → 400 nebo normalizovaný.
- **EC-00-01-12 — Audit zachytí PII? (název kolekce)** · Trigger: create · Očekávané chování: název je business metadata, NE `[PersonalData]`; audit ho ukládá plaintextem do `hybridrag_audit_entries` · Mechanismus: `AuditInterceptor` na SaveChanges, `Name` bez `[PersonalData]` · Severity: P3 · Test: po create audit entry obsahuje name.
- **EC-00-01-13 — Per-tenant kvóta na počet tenant kolekcí** · Trigger: tenant překročí konfiguro­vaný limit (`Rag:Limits:MaxTenantCollections`) · Očekávané chování: 409/422 `rag.collection.quota_exceeded`, explicitní chyba, ne tiché ignorování · Mechanismus: handler spočítá count (LINQ) před insertem; BusinessRuleException · Severity: P2 · Test: limit+1 → chyba.

## UC-00-02 — User vytvoří svou privátní (user-scope) kolekci
- **Actor / role:** user
- **Precondition:** Autentizovaný uživatel (žádná zvláštní permission); aktivní tenant; modul entitled.
- **Trigger:** `POST /v1/hybridrag/collections` (body `{ name, scope: "User" }`)
- **Main flow:** `CreateCollectionCommand(Scope=User)` → handler: `OwnerUserId = ITenantContext.UserId`, `TenantId` z tokenu, insert přes outbox, publikuje `CollectionCreatedIntegrationEvent`, commit. 201 + Location.
- **Postcondition / záruky:** Řádek scope=User, owner_user_id = volající, viditelný JEN vlastníkovi (RLS). Audit entry. Idempotence přes UNIQUE `(tenant_id, scope, owner_user_id, lower(name))`.
- **Tenancy / permissions:** Scope User; bez speciální permission (vlastnictví = identita); custom RLS policy `owner_user_id = principal` zpřístupní řádek jen vlastníkovi; `IUserOwned`-styl RLS přes `app.principal_id` GUC.
- **Reuse / canonical pattern:** `UploadFileHandler.cs:21` (owner z tokenu), `FileObject.cs:15` (`IUserOwned` entity), `RegisterUserHandler.cs:22` (outbox).
- **Data dotčena:** `knowledge_collections` · **Eventy:** `CollectionCreatedIntegrationEvent`
- **Priorita:** P0

### Edge cases UC-00-02
- **EC-00-02-01 — Stejné jméno user-scope u dvou různých uživatelů téhož tenantu** · Trigger: user A i user B vytvoří „Notes“ · Očekávané chování: oba uspějí (201), UNIQUE zahrnuje `owner_user_id` → žádná kolize · Mechanismus: UNIQUE `(tenant_id, scope, owner_user_id, lower(name))` · Severity: P1 · Test: A i B „Notes“ → oba 201, 2 řádky.
- **EC-00-02-02 — Stejné jméno user-scope dvakrát týmž uživatelem** · Trigger: A vytvoří „Notes“ 2× · Očekávané chování: 2. = 409 `rag.collection.name_taken` · Mechanismus: UNIQUE + catch `DbUpdateException` · Severity: P2 · Test: dvojí → 409.
- **EC-00-02-03 — User-scope „Notes“ vs Tenant-scope „Notes“** · Trigger: existuje tenant „Notes“, user vytvoří svou „Notes“ · Očekávané chování: povoleno (různý scope/owner v UNIQUE klíči), retrieval je rozliší podle scope · Mechanismus: UNIQUE klíč zahrnuje `scope` + `owner_user_id` (NULL vs guid) · Severity: P2 · Test: oba scope „Notes“ → 2 řádky.
- **EC-00-02-04 — User se pokusí vytvořit Tenant-scope bez permission** · Trigger: user pošle `scope=Tenant` · Očekávané chování: 403 (permission gating je per-scope) · Mechanismus: endpoint vyžaduje `RagCollectionManageTenant` jen pro tenant-scope; user-scope bez permission. Pokud je endpoint společný, handler/validator hlídá: scope=Tenant ⇒ kontrola permission, jinak Forbidden · Severity: P0 · Test: user scope=Tenant → 403; scope=User → 201.
- **EC-00-02-05 — Cross-tenant IDOR pokus přes spoofnutý token claim** · Trigger: manipulovaný `tenant_id` v JWT · Očekávané chování: token podpis selže (401), žádný zápis · Mechanismus: JWT validace (`TokenIssuer`), tenancy z ověřeného claimu · Severity: P0 · Test: tampered JWT → 401.
- **EC-00-02-06 — RLS izolace: user A nevidí privátní kolekci usera B** · Trigger: A načte detail kolekce B (cizí id) · Očekávané chování: 404 (ne 403 — neprozradit existenci) · Mechanismus: RLS policy `owner_user_id=principal` → 0 rows → NotFound (jako Files cizí id → 404) · Severity: P0 · Test: A GET B.collectionId → 404.
- **EC-00-02-07 — Per-user kvóta kolekcí** · Trigger: user překročí `Rag:Limits:MaxUserCollections` · Očekávané chování: 422 `rag.collection.quota_exceeded` · Mechanismus: handler count LINQ + BusinessRuleException · Severity: P2 · Test: limit+1 → chyba.
- **EC-00-02-08 — Soft-deleted user (erased) vytváří kolekci** · Trigger: token erased účtu · Očekávané chování: 401/403, nelze · Mechanismus: erased/soft-deleted nemůže refresh/login (auth hardening), token nevydán · Severity: P1 · Test: erased token → 401.

## UC-00-03 — Výpis kolekcí (dvouvrstvá viditelnost)
- **Actor / role:** user nebo tenant-admin
- **Precondition:** Autentizovaný uživatel.
- **Trigger:** `GET /v1/hybridrag/collections?scope=&page=&pageSize=`
- **Main flow:** Endpoint → `ListCollectionsQuery(scope filter optional, paging)` → `ListCollectionsHandler` přes `IReadDbContextFactory` (read context, žádná transakce) → LINQ: `tenant_id == claim AND (scope == Tenant OR owner_user_id == principal)`, `OrderBy(Name)`, paging → `Paged<CollectionResponse>` s `totalCount`.
- **Postcondition / záruky:** Vrací sjednocení tenant-shared + vlastní user-scope kolekcí; žádná mutace, žádný event; 200.
- **Tenancy / permissions:** Scope Tenant|User; RLS je defence-in-depth (i kdyby LINQ filtr chyběl, RLS policy stejné pravidlo vynutí); read context použije `app_rls` roli + GUC.
- **Reuse / canonical pattern:** `GetProfileHandler.cs:12` (read query, `IReadDbContextFactory`), paging dle Files list slice.
- **Data dotčena:** `knowledge_collections` (read) · **Eventy:** žádné
- **Priorita:** P0

### Edge cases UC-00-03
- **EC-00-03-01 — User vidí tenant + vlastní, NE cizí user-scope** · Trigger: tenant má 2 tenant koleckce + A má 1 privátní + B má 1 privátní; volá A · Očekávané chování: A dostane 3 (2 tenant + 1 vlastní), NE B privátní · Mechanismus: LINQ filtr `scope=Tenant OR owner=principal` + RLS · Severity: P0 · Test: seed 4, A list → 3, neobsahuje B id.
- **EC-00-03-02 — Filtr `?scope=User`** · Trigger: query scope=User · Očekávané chování: jen vlastní user-scope (žádné tenant) · Mechanismus: handler aplikuje dodatečný `Where(scope==User)` nad bezpečnostním filtrem · Severity: P2 · Test: scope=User → jen owner řádky.
- **EC-00-03-03 — Filtr `?scope=Tenant`** · Trigger: query scope=Tenant · Očekávané chování: jen tenant-shared kolekce · Mechanismus: `Where(scope==Tenant)` · Severity: P2 · Test: scope=Tenant → owner_user_id všude NULL.
- **EC-00-03-04 — Cross-tenant leakage (RLS guard)** · Trigger: tenant T1 list, v DB existují T2 kolekce · Očekávané chování: 0 řádků z T2 · Mechanismus: EF tenancy filtr `TenantId==claim` + custom RLS `tenant_id` predikát (dvouvrstvě) · Severity: P0 · Test: 2 tenanti, T1 list → žádné T2 id.
- **EC-00-03-05 — Prázdný výsledek (nový tenant/uživatel)** · Trigger: žádné kolekce (nebo jen auto-default) · Očekávané chování: 200 s prázdným polem / jen default, `totalCount` korektní · Mechanismus: paging vrací prázdné, ne 404 · Severity: P3 · Test: čerstvý user → 200, items=[default] nebo [].
- **EC-00-03-06 — Paging: pageSize přes max** · Trigger: `pageSize=100000` · Očekávané chování: cap na max (např. 100), `totalCount` přesný · Mechanismus: paging validator clamp (BuildingBlocks paging) · Severity: P2 · Test: pageSize=1e6 → vráceno ≤100.
- **EC-00-03-07 — Paging: stránka mimo rozsah** · Trigger: `page=999` při 3 řádcích · Očekávané chování: 200 prázdné items, `totalCount=3` · Mechanismus: OFFSET za koncem → prázdné · Severity: P3 · Test: page=999 → items=[], totalCount=3.
- **EC-00-03-08 — Negativní / nulové paging hodnoty** · Trigger: `page=0`, `pageSize=-5` · Očekávané chování: 400 nebo normalizace na default · Mechanismus: paging validator `.GreaterThan(0)` · Severity: P3 · Test: záporné → 400/normalizace.
- **EC-00-03-09 — Deterministické řazení při shodných Name** · Trigger: 2 kolekce stejné jméno (jiný scope) · Očekávané chování: stabilní řazení (Name, pak Id) napříč stránkami · Mechanismus: `OrderBy(Name).ThenBy(Id)` (Guid v7 = time-ordered tiebreaker) · Severity: P3 · Test: paging 2× stejné jméno → žádný duplikát/přeskok mezi stránkami.
- **EC-00-03-10 — Soft-deleted kolekce nejsou ve výpisu** · Trigger: kolekce smazána (soft) · Očekávané chování: ve výpisu chybí · Mechanismus: `ISoftDeletable` global query filter (pokud kolekce soft-deletable) / hard delete; viz UC-00-06 · Severity: P1 · Test: po delete → list bez ní.
- **EC-00-03-11 — Query nikdy neotvírá transakci** · Trigger: list · Očekávané chování: žádný outbox/event, čistý read · Mechanismus: zákon §2 (queries jen čtou), `IReadDbContextFactory` · Severity: P2 · Test: list nepublikuje event (ověř wolverine outgoing).
- **EC-00-03-12 — Read-after-write konzistence** · Trigger: create pak ihned list · Očekávané chování: nová kolekce je vidět (read context vidí committed data) · Mechanismus: write commit před list; read context na téže DB · Severity: P2 · Test: create→list → obsahuje nové id.

## UC-00-04 — Detail kolekce (GET by id) + statistiky
- **Actor / role:** user nebo tenant-admin
- **Precondition:** Kolekce existuje a je viditelná volajícímu.
- **Trigger:** `GET /v1/hybridrag/collections/{id}`
- **Main flow:** `GetCollectionQuery(Id)` → read handler → LINQ `FirstOrDefault(c => c.Id==id)` (RLS + tenancy filtr aplikované), volitelně dopočítá `DocumentCount`, `ChunkCount` LINQ agregací. 200 + `CollectionResponse`; nenalezeno → `NotFoundException` → 404.
- **Postcondition / záruky:** Read-only, 200/404, žádný event.
- **Tenancy / permissions:** Scope Tenant|User; cizí/neexistující id → 404 (neprozrazovat existenci).
- **Reuse / canonical pattern:** `GetProfileHandler.cs:12`, `GetOperationStatusEndpoint` (RLS-scoped, cizí id → 404).
- **Data dotčena:** `knowledge_collections`, agregace nad `documents`/`chunks` · **Eventy:** žádné
- **Priorita:** P1

### Edge cases UC-00-04
- **EC-00-04-01 — Cizí user-scope id (IDOR)** · Trigger: A GET B.private · Očekávané chování: 404 · Mechanismus: RLS → 0 rows → NotFoundException · Severity: P0 · Test: A GET B id → 404.
- **EC-00-04-02 — Cross-tenant id** · Trigger: T1 GET T2 kolekci · Očekávané chování: 404 · Mechanismus: tenancy filtr + RLS · Severity: P0 · Test: → 404.
- **EC-00-04-03 — Neexistující / malformed Guid** · Trigger: `{id}=not-a-guid` · Očekávané chování: 400 (route constraint) nebo 404 · Mechanismus: route `:guid` constraint · Severity: P3 · Test: nevalidní → 400.
- **EC-00-04-04 — Statistiky respektují scope chunků** · Trigger: tenant kolekce s chunky více uživatelů · Očekávané chování: count odráží jen viditelné řádky dle RLS volajícího · Mechanismus: agregace nad RLS-filtrovaným setem · Severity: P2 · Test: counts == viditelné chunky.
- **EC-00-04-05 — Soft-deleted dokumenty se nepočítají** · Trigger: kolekce s 1 soft-deleted dokumentem · Očekávané chování: `DocumentCount` je nepočítá · Mechanismus: `ISoftDeletable` filtr · Severity: P2 · Test: 1 deleted → count 0.
- **EC-00-04-06 — Detail soft-deleted kolekce** · Trigger: GET po soft delete · Očekávané chování: 404 · Mechanismus: soft-delete filtr · Severity: P2 · Test: → 404.

## UC-00-05 — Přejmenování kolekce
- **Actor / role:** tenant-admin (tenant-scope) | user (vlastní user-scope)
- **Precondition:** Kolekce existuje a volající má právo (tenant-scope → permission; user-scope → vlastnictví).
- **Trigger:** `PATCH /v1/hybridrag/collections/{id}` (body `{ name }`)
- **Main flow:** `RenameCollectionCommand(Id, NewName)` → `RenameCollectionHandler`: načte tracked entitu (RLS filtruje), ověří right (scope=Tenant ⇒ permission, scope=User ⇒ owner==principal), nastaví `Name`, `SaveChangesAndFlushMessagesAsync()` (xmin serializace + audit změněného pole), publikuje `CollectionRenamedIntegrationEvent`. 200.
- **Postcondition / záruky:** `Name` aktualizováno; audit zaznamená old→new (jen změněné pole → JSONB); event publikován; xmin chrání proti lost update.
- **Tenancy / permissions:** Scope-dependent; RLS zajistí, že cizí kolekci nelze ani načíst (→404).
- **Reuse / canonical pattern:** tracked update + xmin `ConcurrencyRetryBehavior`, audit `AuditInterceptor`; outbox dle `RegisterUserHandler.cs:22`.
- **Data dotčena:** `knowledge_collections`, `hybridrag_audit_entries` · **Eventy:** `CollectionRenamedIntegrationEvent`
- **Priorita:** P2

### Edge cases UC-00-05
- **EC-00-05-01 — Rename na již existující jméno (kolize)** · Trigger: přejmenuj na jméno jiné kolekce v témže scope · Očekávané chování: 409 `rag.collection.name_taken` · Mechanismus: UNIQUE + catch `DbUpdateException` · Severity: P1 · Test: 2 kolekce, rename A→B.name → 409.
- **EC-00-05-02 — Rename na stejnou hodnotu (no-op)** · Trigger: name == current · Očekávané chování: 200, žádná změna auditu (EF nedetekuje změnu) nebo idempotentní event · Mechanismus: change tracker zjistí no-change → SaveChanges nic neorazí · Severity: P3 · Test: rename na totéž → audit beze změny.
- **EC-00-05-03 — Concurrent rename (lost update)** · Trigger: 2 paralelní rename téže kolekce · Očekávané chování: jeden uspěje, druhý retry/přepíše deterministicky, žádná korupce · Mechanismus: xmin + `ConcurrencyRetryBehavior` (5×, clear tracker) · Severity: P1 · Test: 2-way rename → konzistentní finální stav.
- **EC-00-05-04 — User přejmenuje tenant-scope kolekci** · Trigger: user bez permission PATCH tenant kolekci · Očekávané chování: 403 (nebo 404 pokud RLS skryje) · Mechanismus: permission check pro scope=Tenant · Severity: P0 · Test: user PATCH tenant → 403/404.
- **EC-00-05-05 — User přejmenuje cizí user-scope** · Trigger: A PATCH B.private · Očekávané chování: 404 · Mechanismus: RLS → 0 rows · Severity: P0 · Test: → 404.
- **EC-00-05-06 — Prázdný/oversized/invalid Name** · Trigger: viz UC-00-01 validace · Očekávané chování: 400 odpovídající errorCode · Mechanismus: `RenameCollectionValidator` sdílí pravidla · Severity: P2 · Test: prázdný → 400.
- **EC-00-05-07 — Rename nemění Scope ani Owner** · Trigger: body se pokusí přidat `scope`/`ownerUserId` · Očekávané chování: ignorováno, scope/owner immutable (změna scope = bezpečnostní/izolace riziko) · Mechanismus: Command pole nemá; handler nemění scope · Severity: P1 · Test: pokus o scope change → beze změny.

## UC-00-06 — Smazání kolekce (soft delete + kaskáda)
- **Actor / role:** tenant-admin (tenant-scope) | user (vlastní user-scope)
- **Precondition:** Kolekce existuje; volající má právo.
- **Trigger:** `DELETE /v1/hybridrag/collections/{id}`
- **Main flow:** `DeleteCollectionCommand(Id)` → handler: ověří right, soft-delete kolekce (`ISoftDeletable`), publikuje `CollectionDeletedIntegrationEvent` přes outbox; Worker handler asynchronně kaskádně soft-deletuje/odindexuje `Document`/`Chunk`/`GraphNode`/`GraphEdge` patřící kolekci (stale index cleanup). Commit. 202 (kaskáda je durable práce) NEBO 204 (pokud kaskáda jen flag).
- **Postcondition / záruky:** Kolekce není ve výpisu/retrievalu; chunky/embeddingy se přestanou retrievovat (IsCurrent=false / soft delete); audit retain; idempotentní (dvojí delete = no-op).
- **Tenancy / permissions:** Scope-dependent; RLS skryje cizí; kaskáda běží v SYSTEM kontextu Workeru ale scoped na tenant/collection id.
- **Reuse / canonical pattern:** outbox publish `RegisterUserHandler.cs:22`, worker kaskáda dle `ProvisionCreditAccountHandler.cs:13`, durable 202 dle `StartDemoOperationHandler.cs:17`.
- **Data dotčena:** `knowledge_collections`, `documents`, `chunks`, `graph_nodes`, `graph_edges`, blobs přes `IFileStorage` · **Eventy:** `CollectionDeletedIntegrationEvent`
- **Priorita:** P1

### Edge cases UC-00-06
- **EC-00-06-01 — Delete neprázdné kolekce (má dokumenty)** · Trigger: kolekce s 50 dokumenty · Očekávané chování: kolekce + všechny child entity odindexovány; retrieval je přestane vracet · Mechanismus: kaskádový worker handler, durable, idempotentní per child id · Severity: P0 · Test: delete → následný search vrací 0 z té kolekce.
- **EC-00-06-02 — Stale index po delete** · Trigger: search ihned po delete, kaskáda ještě běží · Očekávané chování: smazané chunky se NEvracejí (filtr IsCurrent + soft-delete na úrovni query, ne jen kaskáda) · Mechanismus: retrieval filtruje `collection not soft-deleted` LINQ → izolováno od časování kaskády · Severity: P0 · Test: delete pak hned search → 0 hits z kolekce.
- **EC-00-06-03 — Idempotentní dvojí delete** · Trigger: DELETE 2× · Očekávané chování: 2. = 204/202 no-op (ne 500), žádná duplicitní kaskáda · Mechanismus: soft-delete flag guard + inbox dedup na event MessageId · Severity: P1 · Test: 2× delete → idempotentní.
- **EC-00-06-04 — Delete cizí kolekce (IDOR)** · Trigger: A DELETE B.private nebo cross-tenant · Očekávané chování: 404 · Mechanismus: RLS → 0 rows → NotFound · Severity: P0 · Test: → 404, kolekce nedotčena.
- **EC-00-06-05 — User maže tenant-scope kolekci** · Trigger: user bez permission · Očekávané chování: 403/404 · Mechanismus: permission gating scope=Tenant · Severity: P0 · Test: → 403/404.
- **EC-00-06-06 — Pokus smazat výchozí (auto-provision) tenant kolekci** · Trigger: DELETE default „Company Knowledge“ · Očekávané chování: buď zakázáno (`rag.collection.default_undeletable`) nebo povoleno s re-provision guardem — rozhodnuto: default je chráněný před smazáním · Mechanismus: handler kontroluje `IsDefault` flag → BusinessRuleException · Severity: P2 · Test: delete default → 409.
- **EC-00-06-07 — Crash uprostřed kaskády** · Trigger: Worker spadne po smazání části chunků · Očekávané chování: po restartu kaskáda dokončí (durable), žádné osiřelé chunky natrvalo · Mechanismus: Wolverine retry/DLQ + idempotentní handler per id; volitelně reconciliation job · Severity: P1 · Test: simuluj výjimku → retry dokončí.
- **EC-00-06-08 — Blob orphany ve storage** · Trigger: dokumenty mají blob v `IFileStorage` · Očekávané chování: kaskáda smaže i bloby; selhání storage delete neblokuje DB konzistenci (retry) · Mechanismus: kompenzační delete dle `UploadFileHandler.cs:21` vzoru, `IFileStorage.DeleteAsync` idempotentní · Severity: P2 · Test: po kaskádě blob neexistuje.
- **EC-00-06-09 — Audit zachován i po delete** · Trigger: delete · Očekávané chování: `hybridrag_audit_entries` řádky NEsmazány (AML/forenzika) · Mechanismus: append-only audit, nemažeme · Severity: P1 · Test: po delete audit query vrací historii.
- **EC-00-06-10 — Graf supernode/sdílené uzly** · Trigger: `GraphNode` referencovaný z více kolekcí (sdílená entita) · Očekávané chování: uzel se nesmaže pokud má hrany z jiné žijící kolekce; smažou se jen edges této kolekce · Mechanismus: kaskáda maže edges scoped na collection; node GC jen když 0 zbylých edges (ref-count LINQ) · Severity: P2 · Test: sdílený node přežije delete jedné kolekce.

## UC-00-07 — Auto-provision výchozí tenant kolekce při registraci tenanta
- **Actor / role:** system/worker
- **Precondition:** Nový tenant byl provisionován v Identity (registrace prvního uživatele / tenant creation).
- **Trigger:** integration event `TenantProvisionedIntegrationEvent` (nebo `UserRegisteredIntegrationEvent` s příznakem „tenant owner“) přijatý Workerem.
- **Main flow:** Wolverine doručí event → public handler `ProvisionDefaultCollectionHandler.Handle(evt, IDispatcher, ct)` → `dispatcher.Send(CreateCollectionCommand(Scope=Tenant, Name="Company Knowledge", IsDefault=true, TenantId=evt.TenantId))` → handler insert přes outbox; idempotence přes UNIQUE `(tenant_id, scope, IsDefault)` nebo `(tenant_id, scope, lower(name))`.
- **Postcondition / záruky:** Každý tenant má právě 1 default tenant-scope kolekci; opakované doručení eventu nezaloží duplikát; kolekce je `IsDefault`.
- **Tenancy / permissions:** SYSTEM kontext (worker) — `TenantId` z eventu, NE z HttpContext; `HttpTenantContext`/`SystemTenantContext` pro stamping; žádná uživatelská permission (system bootstrap).
- **Reuse / canonical pattern:** worker handler shell `ProvisionCreditAccountHandler.cs:13` (Billing provisionuje credit account na `UserRegistered`); cross-module wiring `ConfigureMessaging` + `Discovery.IncludeType`.
- **Data dotčena:** `knowledge_collections` · **Eventy:** konzumuje `TenantProvisionedIntegrationEvent`; publikuje `CollectionCreatedIntegrationEvent`
- **Priorita:** P1

### Edge cases UC-00-07
- **EC-00-07-01 — Duplicitní doručení eventu (at-least-once)** · Trigger: stejný event 2× · Očekávané chování: 1 default kolekce, druhý = no-op · Mechanismus: Wolverine inbox dedup (UNIQUE MessageId) + UNIQUE business key + catch `DbUpdateException` · Severity: P0 · Test: replay eventu → count(default)=1.
- **EC-00-07-02 — Více uživatelů registruje „svůj“ tenant současně (race)** · Trigger: 2 eventy pro stejný tenant_id (např. owner + invite) · Očekávané chování: přesně 1 default · Mechanismus: UNIQUE `(tenant_id, scope, IsDefault=true)` partial unique index · Severity: P1 · Test: 2 paralelní → 1 řádek.
- **EC-00-07-03 — Handler resolvuje scoped service (Wolverine gotcha)** · Trigger: handler injektuje `IDispatcher` · Očekávané chování: handler SE vygeneruje a spustí · Mechanismus: `ServiceLocationPolicy.AlwaysAllowed` (CLAUDE.md #1 gotcha) · Severity: P0 · Test: po eventu default existuje (ne jen „Handled“).
- **EC-00-07-04 — Tenant entitlement HybridRag je vypnutý** · Trigger: tenant bez modulu · Očekávané chování: žádná kolekce se nezaloží (modul není entitled) / nebo se založí lazy při prvním entitlementu · Mechanismus: handler kontroluje entitlement před insertem; rozhodnutí: skip pokud not-entitled · Severity: P2 · Test: not-entitled tenant → 0 kolekcí.
- **EC-00-07-05 — Event přijde, ale tenant byl mezitím smazán** · Trigger: tenant deprovision před zpracováním · Očekávané chování: handler neselže fatálně; insert respektuje FK/RLS, případně skip · Mechanismus: idempotentní guard; chybějící tenant → no-op nebo dead-letter (ne crash loop) · Severity: P2 · Test: smazaný tenant event → bez crash.
- **EC-00-07-06 — Lokalizace názvu default kolekce** · Trigger: tenant locale cs vs en · Očekávané chování: konzistentní stabilní Name (např. vždy „Company Knowledge“ jako stabilní klíč, lokalizace jen v UI) · Mechanismus: persist neutrální název, i18n v prezentaci · Severity: P3 · Test: cs i en tenant → stejný persisted Name.
- **EC-00-07-07 — Handler je idempotentní a order-independent** · Trigger: event přijde před/po jiných provisioning eventech · Očekávané chování: nezávisí na pořadí · Mechanismus: zákon — handlery idempotentní + order-independent · Severity: P1 · Test: přehoď pořadí eventů → stejný výsledek.

## UC-00-08 — Přiřazení dokumentu do kolekce při ingestu
- **Actor / role:** user (vlastní dokument) | tenant-admin (tenant korpus)
- **Precondition:** Cílová kolekce existuje a je zapisovatelná volajícím; ingest endpoint (Oblast 01) předává `CollectionId`.
- **Trigger:** `POST /v1/hybridrag/collections/{collectionId}/documents` (multipart) — vstupní bod ingestu z pohledu vlastnictví.
- **Main flow:** Endpoint → `IngestDocumentCommand(CollectionId, file…)` → handler ověří, že kolekce je viditelná+zapisovatelná (RLS load + scope/owner check), odvodí `Document.Scope` a `Document.OwnerUserId` Z KOLEKCE (ne z requestu), uloží blob přes `IFileStorage` (server-generated key), vloží `Document(Status=Pending)`, nastartuje `IngestSaga` přes outbox. 202 + Location na document status.
- **Postcondition / záruky:** Dokument dědí scope/owner kolekce → konzistentní izolace child chunků; idempotence ingestu řeší Oblast 01 (content hash key).
- **Tenancy / permissions:** Zápis do tenant kolekce vyžaduje `RagCollectionManageTenant` (nebo `RagIngest` permission); do vlastní user kolekce stačí vlastnictví; cizí kolekce → 404.
- **Reuse / canonical pattern:** `UploadFileHandler.cs:21` (blob+metadata split, owner z tokenu/kontextu), saga start dle `CreditPurchaseSaga.cs:30`.
- **Data dotčena:** `documents`, `knowledge_collections` (read), blob storage · **Eventy:** `DocumentIngestStartedIntegrationEvent`
- **Priorita:** P1

### Edge cases UC-00-08
- **EC-00-08-01 — Ingest do cizí/neexistující kolekce** · Trigger: `collectionId` patří jinému tenantu/uživateli · Očekávané chování: 404, žádný blob ani dokument · Mechanismus: RLS load kolekce → 0 rows → NotFound PŘED uložením blobu · Severity: P0 · Test: cizí collectionId → 404, storage prázdné.
- **EC-00-08-02 — Dokument NEDĚDÍ scope z requestu (privilege bypass)** · Trigger: body se pokusí nastavit `scope=Tenant` při user kolekci · Očekávané chování: scope se odvodí z KOLEKCE, request ignorován · Mechanismus: handler kopíruje `Scope/OwnerUserId` z entity kolekce · Severity: P0 · Test: pokus o eskalaci scope → dokument má scope kolekce.
- **EC-00-08-03 — User ingestuje do tenant kolekce bez permission** · Trigger: běžný user POST do tenant kolekce · Očekávané chování: 403 (nebo povoleno dle `RagIngest` policy — rozhodnuto: tenant zápis gated) · Mechanismus: permission/scope check · Severity: P1 · Test: user→tenant kolekce → 403.
- **EC-00-08-04 — Blob uložen, DB insert selže (kompenzace)** · Trigger: výjimka po `PutAsync` před commit · Očekávané chování: blob se kompenzačně smaže, žádný orphan · Mechanismus: kompenzační delete dle `UploadFileHandler.cs:21` · Severity: P1 · Test: simuluj DB fail → blob neexistuje.
- **EC-00-08-05 — Souběžný ingest do mazané kolekce** · Trigger: kolekce se právě maže · Očekávané chování: buď 404/409, nebo dokument je následně odindexován kaskádou; žádný „živý“ dokument v mrtvé kolekci · Mechanismus: RLS/soft-delete filtr + kaskáda race-safe (idempotentní) · Severity: P2 · Test: delete||ingest → výsledek konzistentní (dokument neviditelný).
- **EC-00-08-06 — MIME/velikost guard** · Trigger: unsupported MIME / oversized · Očekávané chování: 400 allowlist/size errorCode (deleguje na Oblast 01 policy) · Mechanismus: `UploadFileValidator`-styl allowlist + size cap · Severity: P1 · Test: exe → 400.
- **EC-00-08-07 — Storage key je server-generated** · Trigger: klient pošle filename `../../etc/passwd` · Očekávané chování: key = `{ownerOrTenant}/{docId:N}`, filename jen metadata · Mechanismus: server-generated key + `StorageKey.Validate` path-traversal guard · Severity: P0 · Test: traversal filename → key neobsahuje cestu klienta.

## UC-00-09 — Přesun dokumentu mezi kolekcemi
- **Actor / role:** user | tenant-admin
- **Precondition:** Zdrojová i cílová kolekce viditelné+zapisovatelné volajícím.
- **Trigger:** `PATCH /v1/hybridrag/documents/{id}` (body `{ targetCollectionId }`)
- **Main flow:** `MoveDocumentCommand(DocumentId, TargetCollectionId)` → handler: načte dokument (RLS) + cílovou kolekci (RLS), ověří right na obě, přepíše `Document.CollectionId` a — pokud se mění scope/owner — i `Scope`/`OwnerUserId` dokumentu A VŠECH jeho `Chunk`/`GraphNode`/`GraphEdge` (re-stamp), přes outbox publikuje `DocumentMovedIntegrationEvent`; re-stamp child řádků je durable worker práce (202) kvůli možnému velkému počtu chunků. Commit.
- **Postcondition / záruky:** Dokument i child entity konzistentně přestampovány na cílový scope/owner → retrieval izolace zůstává korektní; xmin/atomic guard chrání souběh.
- **Tenancy / permissions:** Přesun do/z tenant kolekce vyžaduje `RagCollectionManageTenant`; cross-scope přesun (User→Tenant „publish“ / Tenant→User) je permission-gated a auditovaný; cizí kolekce/dokument → 404.
- **Reuse / canonical pattern:** tracked update + xmin, durable child re-stamp dle `StartDemoOperationHandler.cs:17` + worker `ProvisionCreditAccountHandler.cs:13`.
- **Data dotčena:** `documents`, `chunks`, `graph_nodes`, `graph_edges` · **Eventy:** `DocumentMovedIntegrationEvent`
- **Priorita:** P2

### Edge cases UC-00-09
- **EC-00-09-01 — Cross-scope publish (User→Tenant) zpřístupní data celé firmě** · Trigger: user přesune privátní dokument do tenant kolekce · Očekávané chování: povoleno JEN s permission + audit; child chunky přestampovány na scope=Tenant, owner=NULL → nově viditelné v tenant searchi · Mechanismus: permission gating + re-stamp + audit; explicitní akce, ne tichá · Severity: P0 · Test: bez permission → 403; s permission → chunky scope=Tenant.
- **EC-00-09-02 — Cross-scope unpublish (Tenant→User) skryje firemní data jednomu uživateli** · Trigger: tenant dokument → user kolekce · Očekávané chování: gated; re-stamp na owner=mover → ostatní v tenantu už dokument nevidí · Mechanismus: permission + audit; riziko ztráty firemního přístupu → vyžaduje admin · Severity: P1 · Test: po přesunu jiný user → 0 hits.
- **EC-00-09-03 — Přesun do cizí kolekce** · Trigger: target patří jinému uživateli/tenantu · Očekávané chování: 404 · Mechanismus: RLS load target → 0 rows · Severity: P0 · Test: → 404.
- **EC-00-09-04 — Přesun do téže kolekce (no-op)** · Trigger: target == current · Očekávané chování: 200 no-op, žádné zbytečné re-stampy/eventy · Mechanismus: handler early-return při shodě · Severity: P3 · Test: same → no event.
- **EC-00-09-05 — Souběžný přesun + ingest chunků (race na child re-stamp)** · Trigger: ingest saga přidává chunky během přesunu · Očekávané chování: nově vzniklé chunky musí skončit ve správném (cílovém) scope; žádný „polovičatý“ stav · Mechanismus: re-stamp worker je idempotentní + opakuje sweep dokud nejsou všechny child na cílovém scope (reconciliation), nebo přesun blokuje dokud `Status != Processing` · Severity: P1 · Test: ingest||move → všechny chunky cílový scope.
- **EC-00-09-06 — Crash během re-stampu child entit** · Trigger: worker spadne po části chunků · Očekávané chování: po restartu dokončí; žádné chunky se „smíšeným“ scope natrvalo · Mechanismus: durable + idempotentní per-id `ExecuteUpdate` guard `WHERE scope != target` · Severity: P1 · Test: výjimka uprostřed → retry dokončí, 0 mismatch.
- **EC-00-09-07 — Supernode/sdílené graph uzly při přesunu** · Trigger: dokument sdílí entity uzly s jinými dokumenty · Očekávané chování: re-stamp scope se aplikuje opatrně — sdílené uzly se neprivatizují, pokud je drží i jiný dokument · Mechanismus: ref-count LINQ, re-stamp jen exkluzivních uzlů · Severity: P2 · Test: sdílený node po přesunu stále viditelný správně.
- **EC-00-09-08 — Concurrent dvojí move (lost update)** · Trigger: 2 paralelní move téhož dokumentu · Očekávané chování: deterministický finální `CollectionId`, žádná korupce · Mechanismus: xmin + `ConcurrencyRetryBehavior` · Severity: P1 · Test: 2-way move → konzistence.
- **EC-00-09-09 — Move soft-deleted dokumentu** · Trigger: dokument je smazaný · Očekávané chování: 404 · Mechanismus: soft-delete filtr · Severity: P2 · Test: → 404.
- **EC-00-09-10 — Audit cross-scope přesunu** · Trigger: jakýkoli cross-scope move · Očekávané chování: audit zaznamená kdo, kdy, z jakého do jakého scope (forenzika úniku) · Mechanismus: `AuditInterceptor` na změněných polích + explicitní event · Severity: P1 · Test: po move audit obsahuje old/new scope+collection.

## UC-00-10 — Dvouvrstvá RLS izolace kolekcí (DB-level guard, defence-in-depth)
- **Actor / role:** system (RLS bootstrap) — ověřuje izolaci, ne uživatelská akce
- **Precondition:** Migrace `HybridRag` aplikovány; `RlsBootstrapper` nasadil custom policy na `knowledge_collections` (a child tabulky); app data connection běží pod `app_rls` rolí s GUC `app.tenant_id` + `app.principal_id`.
- **Trigger:** jakýkoli read/write nad kolekcemi přes `app_rls` roli (každý request).
- **Main flow:** `PrincipalSessionConnectionInterceptor` orazí GUCy z tokenu → každý SQL nad `knowledge_collections` je filtrován RLS policy `tenant_id = current_setting('app.tenant_id') AND (scope = 'Tenant' OR owner_user_id = current_setting('app.principal_id'))` (USING + WITH CHECK) → i kdyby EF LINQ filtr chyběl, DB řádky neunikne.
- **Postcondition / záruky:** Žádný leak cizího tenantu ani cizí user-scope kolekce ani přes zapomenutý `WHERE`; zápis nelze nasměrovat mimo vlastní tenant/scope (WITH CHECK).
- **Tenancy / permissions:** dvouvrstvé — `ITenantScoped` EF filtr (app-level) + custom RLS policy (DB-level); migrace běží na admin connection (BYPASSRLS), runtime na `app_rls`.
- **Reuse / canonical pattern:** `RlsBootstrapper` + `PrincipalSessionConnectionInterceptor` (Persistence building-block), custom policy migrace (jediný povolený raw DDL vedle pgvector extension).
- **Data dotčena:** `knowledge_collections`, `documents`, `chunks`, `graph_nodes`, `graph_edges` · **Eventy:** žádné
- **Priorita:** P0

### Edge cases UC-00-10
- **EC-00-10-01 — Zapomenutý `WHERE owner` v novém handleru** · Trigger: vývojář napíše dotaz bez user filtru · Očekávané chování: cizí řádky stejně neuniknou · Mechanismus: RLS USING policy na DB · Severity: P0 · Test: handler bez filtru → vrátí jen povolené (test s 2 usery).
- **EC-00-10-02 — WITH CHECK brání zápisu mimo scope** · Trigger: pokus vložit kolekci s cizím `tenant_id`/`owner_user_id` · Očekávané chování: DB odmítne (RLS violation) · Mechanismus: WITH CHECK predikát · Severity: P0 · Test: insert cizí tenant_id přes app_rls → chyba.
- **EC-00-10-03 — Tenant-scope řádek viditelný všem v tenantu, ne mimo** · Trigger: dva useři téhož tenantu + jeden cizí tenant · Očekávané chování: oba domácí vidí tenant kolekci, cizí ne · Mechanismus: policy `scope='Tenant'` větev omezená `tenant_id` predikátem · Severity: P0 · Test: 3 useři → 2 vidí, 1 ne.
- **EC-00-10-04 — Migrace neběží pod `app_rls`** · Trigger: pokus aplikovat DDL runtime rolí · Očekávané chování: selže/zakázáno; migrace jen admin connection · Mechanismus: `PlatformMigrator` na admin conn (CLAUDE.md §7) · Severity: P1 · Test: migrace pod app_rls → DDL denied.
- **EC-00-10-05 — Chybějící GUC (principal_id nenastaven)** · Trigger: connection bez orazítkování · Očekávané chování: 0 řádků (fail-closed), NE všechny · Mechanismus: `current_setting('app.principal_id', true)` NULL → predikát false; nikdy fail-open · Severity: P0 · Test: bez GUC → 0 rows.
- **EC-00-10-06 — `FORCE ROW LEVEL SECURITY` i pro vlastníka tabulky** · Trigger: app_rls je i owner · Očekávané chování: RLS platí i na ownera (žádný bypass) · Mechanismus: `FORCE RLS` na tabulce · Severity: P1 · Test: owner-styl dotaz stále filtrován.
- **EC-00-10-07 — RLS vypnuto konfigurací (managed DB)** · Trigger: `Persistence:Rls:Enabled=false` · Očekávané chování: app-level EF filtry (`ITenantScoped` + LINQ scope) zůstávají jedinou ochranou; explicitně zdokumentováno · Mechanismus: fallback na admin conn + app filtry (CLAUDE.md) · Severity: P1 · Test: RLS off → EF filtr stále izoluje (ale bez DB guardu).
- **EC-00-10-08 — Child entity (chunks) sdílí stejnou dvouvrstvou politiku** · Trigger: dotaz na chunky cizí user kolekce · Očekávané chování: 0 řádků · Mechanismus: stejná RLS policy aplikovaná na `chunks` (scope/owner kopírované z dokumentu) · Severity: P0 · Test: cizí chunk query → 0.

## UC-00-11 — GDPR erasure dopad na user-scope kolekce a obsah
- **Actor / role:** system/worker (GDPR fan-out)
- **Precondition:** Uživatel požádal o výmaz; `UserErasureRequested` event doručen; `HybridRag` registruje `IErasePersonalData`.
- **Trigger:** integration event `UserErasureRequested` (Worker) → `HybridRagPersonalDataEraser`.
- **Main flow:** Eraser pro daný subjectId: soft-delete/anonymizace user-scope kolekcí daného uživatele, smazání jejich `Document`/`Chunk` blobů + řádků (chunky obsahují `[Encrypted][PersonalData] Content`), crypto-shred DEK subjektu (audit PII se stane `[erased]`); audit/append-only metadata se NEmaže. Idempotentní.
- **Postcondition / záruky:** Privátní korpus uživatele přestane být retrievable; PII v chuncích nečitelné (DEK shred); tenant-scope kolekce, které uživatel jen vytvořil jako admin, zůstávají (firemní vlastnictví) — řeší se anonymizací `OwnerUserId`/audit, ne smazáním firemních dat.
- **Tenancy / permissions:** SYSTEM kontext; per-modul `IErasePersonalData` (zákon GDPR fan-out); shred přes `IPersonalDataProtector`.
- **Reuse / canonical pattern:** `IExportPersonalData`/`IErasePersonalData` registrace v `RegisterServices` (Billing/Notifications vzor), crypto-shred (`ShredSubjectKey`).
- **Data dotčena:** `knowledge_collections`, `documents`, `chunks`, `subject_keys`, blob storage · **Eventy:** konzumuje `UserErasureRequested`
- **Priorita:** P1

### Edge cases UC-00-11
- **EC-00-11-01 — Tenant-scope kolekce vytvořené erasovaným adminem** · Trigger: erase admina, který vlastnil firemní kolekce · Očekávané chování: kolekce zůstává (firemní data), `OwnerUserId` byl stejně NULL pro tenant-scope; žádný firemní výpadek · Mechanismus: tenant-scope owner=NULL → erase se ho netýká · Severity: P1 · Test: erase → tenant kolekce nedotčena.
- **EC-00-11-02 — Idempotentní opakovaná erasure** · Trigger: event 2× · Očekávané chování: 2. = no-op · Mechanismus: inbox dedup + idempotentní handler · Severity: P1 · Test: replay → konzistentní.
- **EC-00-11-03 — Chunk PII nečitelné po shred** · Trigger: po erase čtení chunku · Očekávané chování: dešifrace selže → `[erased]`, ne plaintext · Mechanismus: DEK shred + model converter `[erased]` · Severity: P0 · Test: read chunk po erase → [erased].
- **EC-00-11-04 — Retrieval po erase nevrací user korpus** · Trigger: search po erase · Očekávané chování: 0 hits z erasovaných privátních kolekcí · Mechanismus: soft-delete/odindexace user-scope chunků · Severity: P1 · Test: search → 0.
- **EC-00-11-05 — Audit zachován (AML/tax)** · Trigger: erase · Očekávané chování: `hybridrag_audit_entries` nezmizí, jen PII hodnoty `[erased]` · Mechanismus: append-only audit + crypto-shred PII · Severity: P1 · Test: audit existuje, PII pole [erased].
- **EC-00-11-06 — Export před erase (right to access)** · Trigger: `IExportPersonalData` volán · Očekávané chování: export user-scope kolekcí + dokumentů (metadata + obsah) subjektu · Mechanismus: per-modul exporter LINQ scoped na owner · Severity: P2 · Test: export obsahuje user kolekce, ne cizí.

## UC-00-12 — Tenant/firemní search nad korpusem všech uživatelů (permission-gated viditelnost)
- **Actor / role:** tenant-admin
- **Precondition:** Volající má `RagTenantSearchAllUsers`; cílem je číst napříč user-scope korpusy ve firmě (compliance/discovery).
- **Trigger:** parametr `scope=company` na list/detail kolekcí (nebo dedikovaný admin endpoint) — z pohledu Oblasti 00 jde o rozšířenou viditelnost vlastnictví.
- **Main flow:** `ListCollectionsQuery(includeAllUsers=true)` → handler ověří `RagTenantSearchAllUsers` → použije ELEVATED čtecí cestu, která RLS user-owner predikát rozšíří na celý tenant (viz EC níže), vrátí i cizí user-scope kolekce v rámci tenantu. 200.
- **Postcondition / záruky:** Bez permission se chová jako UC-00-03 (jen vlastní + tenant); s permission vidí všechny user-scope v tenantu, NIKDY mimo tenant.
- **Tenancy / permissions:** Scope Tenant (cross-user, intra-tenant); vyžaduje `RagTenantSearchAllUsers`; cross-tenant zůstává zakázán vždy.
- **Reuse / canonical pattern:** permission gating `.RequirePermission(...)`, elevated čtecí cesta jako forenzní audit read (`AuditRead`) vzor.
- **Data dotčena:** `knowledge_collections` (read) · **Eventy:** žádné
- **Priorita:** P2

### Edge cases UC-00-12
- **EC-00-12-01 — Bez permission ignoruje `includeAllUsers`** · Trigger: běžný user pošle `scope=company` · Očekávané chování: parametr ignorován/403, vidí jen vlastní+tenant · Mechanismus: permission check; default-deny · Severity: P0 · Test: user company → jen vlastní+tenant.
- **EC-00-12-02 — Cross-tenant nikdy nepovoleno ani adminovi** · Trigger: admin T1 s permission · Očekávané chování: nevidí T2 user kolekce · Mechanismus: `tenant_id` predikát zůstává i v elevated cestě (rozšiřuje se jen owner větev, ne tenant) · Severity: P0 · Test: admin T1 → 0 z T2.
- **EC-00-12-03 — Elevated cesta NEobchází tenant RLS** · Trigger: implementace elevace · Očekávané chování: elevace nastaví GUC tak, že user-owner predikát je permisivní, ale tenant predikát drží; NIKDY se nepřipojí jako superuser/BYPASSRLS · Mechanismus: dedikovaná policy větev `is_company_reader` GUC + tenant guard; ne admin connection · Severity: P0 · Test: elevated query nevrací cross-tenant.
- **EC-00-12-04 — Audit elevated čtení** · Trigger: admin čte cizí user korpus · Očekávané chování: zaznamenáno (kdo četl čí data) · Mechanismus: audit/observability na company-search cestě · Severity: P1 · Test: po čtení audit/metric `platform.hybridrag.company_read`.
- **EC-00-12-05 — Privacy-by-default u user-scope** · Trigger: bez explicitního company flagu · Očekávané chování: user-scope je defaultně soukromý (within-tenant privacy) · Mechanismus: dvouvrstvá RLS owner predikát · Severity: P0 · Test: admin bez company flag → cizí user kolekce skryté.

## UC-00-13 — MCP klient spravuje kolekce (trust-boundary identity)
- **Actor / role:** MCP klient (agent jménem uživatele)
- **Precondition:** MCP tool-call mapuje na kolekční operace (list/create/assign) skrz tentýž `/v1` API s tokenem uživatele.
- **Trigger:** MCP tool `hybridrag.collections.*` → interně tentýž endpoint/dispatcher.
- **Main flow:** MCP server přeloží tool-call → autentizovaný HTTP request s tokenem → standardní slice (UC-00-01..03). Identita VŽDY z tokenu, NIKDY z argumentu tool-callu.
- **Postcondition / záruky:** MCP nemůže eskalovat scope/tenant přes argumenty; chování identické s lidským uživatelem.
- **Tenancy / permissions:** Scope/permission dle tokenu MCP session; argumenty tool-callu jsou jen business parametry (name, scope volba), ne identita.
- **Reuse / canonical pattern:** identita `ITenantContext.UserId` (zákon §10) — argument LLM/tool NIKDY jako subjekt.
- **Data dotčena:** `knowledge_collections` · **Eventy:** dle mapované operace
- **Priorita:** P1

### Edge cases UC-00-13
- **EC-00-13-01 — Tool argument obsahuje `tenantId`/`ownerUserId`** · Trigger: LLM vygeneruje argument s cizí identitou · Očekávané chování: ignorováno, použije se token (žádný leak) · Mechanismus: handler bere identitu z `ITenantContext`, ne z argumentu (trust-boundary) · Severity: P0 · Test: tool-call s cizím tenantId → řádek má token tenant.
- **EC-00-13-02 — Indirect prompt injection přes název kolekce** · Trigger: ingestovaný dokument navádí agenta vytvořit/sdílet kolekci · Očekávané chování: stejné permission gating jako u člověka; injection nemůže obejít `RagCollectionManageTenant` · Mechanismus: server-side permission, ne LLM uvážení · Severity: P0 · Test: injection prompt → create tenant bez permission stále 403.
- **EC-00-13-03 — MCP session bez tenant claim** · Trigger: nevalidní/anon MCP session · Očekávané chování: 401, žádná operace · Mechanismus: JWT/tenancy validace · Severity: P0 · Test: anon MCP → 401.
- **EC-00-13-04 — Rate-limit MCP bulk vytváření (DoS)** · Trigger: agent vytvoří 1000 kolekcí · Očekávané chování: 429 + Retry-After / kvóta · Mechanismus: per-user rate limiter + kvóta (EC-00-01-13) · Severity: P2 · Test: burst → 429.


---

## Doplňky z completeness review
- **EC-00-06-11 — Smazání kolekce souběžně s běžící IngestSaga (resurrekce chunků)** · Trigger: kolekce smazána (UC-00-06 kaskáda nastaví child `IsCurrent=false`), ale dokument v ní má rozběhnutou `IngestSaga`, která krátce poté provede atomický `IsCurrent` flip nové generace na `true` (UC-04-06) → smazané chunky znovu retrievovatelné · Očekávané chování: flip ve fázi Index MUSÍ re-checkovat, že kolekce/dokument nejsou soft-deleted (guard ve flip transakci), jinak kaskáda + saga závodí a obsah „obživne“; saga při zjištění smazané kolekce přejde do Abandoned + cleanup · Mechanismus: re-check `collection.IsDeleted`/`document.IsDeleted` ve flip transakci (stale-index guard) + saga terminal guard `CreditPurchaseSaga.cs:30` · Severity: P0 · Test: integ — delete kolekce v okně mezi Embedding a Index flip → po doběhu 0 `IsCurrent=true` chunků smazané kolekce, search 0 hitů.
- **UC-00-14 — Odebrání HybridRag entitlementu tenantovi (lifecycle existujících dat)**
- **EC-00-14-01 — Entitlement vypnut po naindexování dat** · Trigger: tenant měl `HybridRag` entitled, nahrál korpus, admin odebere entitlement · Očekávané chování: deterministicky zdokumentované — všechny `/collections|documents|search` endpointy vrací 404 (`ModuleEntitlementGuard`) na CELÉM module prefixu (ne jen create), data se NEMAŽOU (zachována pro re-entitlement), žádný retrieval · Mechanismus: `ModuleEntitlementGuard` → 404 (multitenancy doc) · Severity: P1 · Test: integ — entitlement off po ingestu → search 404, řádky v DB stále existují.
- **EC-00-14-02 — Běžící ingest saga při odebrání entitlementu** · Trigger: entitlement odebrán uprostřed ingestu · Očekávané chování: saga doběhne v SYSTEM kontextu (entitlement je HTTP-edge guard, ne saga guard) nebo se řízeně abandonuje; žádný zaseknutý Running polostav · Mechanismus: SYSTEM kontext nezávislý na HTTP entitlement guardu; rozhodnutí dokumentováno · Severity: P1 · Test: integ — entitlement off během ingestu → saga terminalizuje, žádné orphan Running.
