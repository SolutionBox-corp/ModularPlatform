# HybridRag — kanonické konvence (zdroj pravdy)

UC/EC katalog (oblasti 00–23) generovalo 24 nezávislých agentů paralelně, takže jednotlivé soubory místy
**driftují v pojmenování** (route prefix, názvy entit/tabulek, permission konstanty, enum hodnoty). Tento soubor
**zmrazuje kanonickou volbu** — při implementaci platí TOHLE, ne zdrojový drift v konkrétním souboru. Sjednocení nálezů
z completeness-critic passu (34 konzistenčních poznámek). **Kde se katalog rozchází, vyhrává tento dokument.**

---

## 1. Pojmenování modulu

| Co | Kanonická hodnota | Drift v katalogu (NEpoužívat) |
|---|---|---|
| Modul / assembly | `ModularPlatform.HybridRag` | — |
| `IModule` | `HybridRagModule` | `RagModule` |
| DbContext | `HybridRagDbContext` | `RagDbContext` |
| Options + validátory | `HybridRag*Options` / `*Validator` | `Rag*Options` |
| Config enable flag | `Modules:HybridRag:Enabled` | `Modules:Rag:*` |
| Entitlement klíč | `hybridrag` | — |
| Cron namespace | `Modules:HybridRag:Jobs:*` | `Rag:Jobs:*` (chybí `Modules:` prefix) |
| Audit tabulka | `hybridrag_audit_entries` (auto dle `ModuleName="hybridrag"`) | — |
| Table prefix | **`hybridrag_*`** (všechny tabulky) | holé `knowledge_collections`/`chunks`/`graph_nodes` |
| Route prefix | **`/v1/hybridrag/...`** (host `MapGroup("/v1")` + relativní `/hybridrag/...`) | `/v1/rag/...` |

## 2. Kanonické routes (relativní pod `/v1`)

| Akce | Route |
|---|---|
| Collections CRUD | `/hybridrag/collections`, `/hybridrag/collections/{id}` |
| Document upload/list | `/hybridrag/documents`, `/hybridrag/collections/{id}/documents` |
| **Hlavní RAG odpověď** | `POST /hybridrag/query` (NE `/ask`, `/answer`) |
| Streaming odpověď | `POST /hybridrag/query/stream` (SSE delta/done) |
| Retrieval-only search | `POST /hybridrag/search` |
| Graf local-expand | `POST /hybridrag/search/graph/local` |
| Reindex | `POST /hybridrag/collections/{id}/reindex` (202 + Operation) |
| Admin audit | `GET /hybridrag/admin/users/{id}/audit` |
| GDPR self | `GET /hybridrag/me/data-summary` |

## 3. Entity model — kanonický výčet (rozšiřuje frozen 7)

**Core (retrieval/graf):** `KnowledgeCollection`, `Document`, `Chunk`, `GraphNode`, `GraphEdge`, `EntityAlias`, `IngestSaga`.
**Core (konverzace/answer — jsou CORE, ne pomocné; dostávají `IDataSubject` + `[Encrypted]` na text):** `RagConversation`,
`RagConversationTurn`, `RagAnswerCitation`, `RagAnswerTrace`.
**Provozní/auxiliary (read-modely, ne PII subjekty):** `eval_samples`, `rag_usage_daily`, `rag_query_trace`,
`rag_cost_ledger`, `rag_abuse_flag`, `rag_budget_alert_state`.
**Konfigurace/audit:** `RagSetting` (`ITenantScoped`, per-tenant/collection override knobů, auditovaný přes `AuditInterceptor`
— oblast 24/25). Audit operací (search/answer/ingest) + config změn jde do `hybridrag_audit_entries`; query-text je `[PersonalData]`
(crypto-shred) — oblast 25.

Tabulky vždy `hybridrag_` prefix: `hybridrag_collections`, `hybridrag_documents`, `hybridrag_chunks`,
`hybridrag_graph_nodes`, `hybridrag_graph_edges`, `hybridrag_entity_aliases`, `hybridrag_ingest_sagas`,
`hybridrag_conversations`, `hybridrag_conversation_turns`, `hybridrag_eval_samples`, `hybridrag_usage_daily`, …

**Property/sloupec sjednocení:**
- Embedding property = **`Chunk.Embedding`** (NE `Chunk.Vector`).
- **Typ embedding sloupce = `halfvec(3072)`** (NE `vector(3072)`). Důvod: pgvector HNSW index má limit 2000 dim pro `vector`;
  `halfvec` podporuje až 4000 dim → HNSW nad 3072-dim jde JEN s `halfvec`. `vector(3072)` v katalogu (EC-03-01-07,
  EC-04-05-02, UC-05-01) je **chyba** → čti jako `halfvec(3072)`.
- Graf provenance (potvrzeno jako součást schématu, nutné pro selektivní delete/erasure/reconcile):
  `GraphEdge.SourceDocumentId`, `GraphNode` provenance/refcount (`SourceDocumentIds` nebo refcount sloupec).

## 4. `Document.Status` — zmrazený stavový automat

`Pending → Extracting → Extracted → Chunking → Chunked → Contextualizing → Embedding → Indexing → Indexed`
+ terminální `Failed` (s `FailureReason`) · zvláštní `NoText` (nešel extrahovat text). Drift (`Uploaded`/`Completed`/
`NeedsOcr`) → mapuj na tento výčet. Saga stavy zrcadlí tyto fáze.

## 5. Idempotency & UNIQUE klíče

- Generace ingestu = **`IngestRunId`** (jeden název; NE „gen" vs „ingest_run_id").
- Chunk UNIQUE = `(document_id, ingest_run_id, ordinal)`; embed idempotency key = `embed:{documentId}:{ingestRunId}:{chunkHash}`.
- Document dedup UNIQUE = `(collection_id, content_hash)` WHERE `is_current` (NE filename).
- Collection UNIQUE = `(tenant_id, scope, owner_user_id, lower(name))`.

## 6. Permissions — jedna sada `PlatformPermissions.Rag*` (PascalCase const + dotted claim)

| Const | Dotted claim | Účel |
|---|---|---|
| `RagSearch` | `rag.search` | základní search (pokud se rozhodne, že existuje — viz Open #5) |
| `RagSearchTenantWide` | `rag.search.tenant_wide` | firemní search napříč všemi usery tenantu |
| `RagCollectionManage` | `rag.collection.manage` | správa kolekcí |
| `RagDocumentWrite` | `rag.document.write` | ingest/upload |
| `RagReindex` | `rag.reindex` | reindex |
| `RagEntityResolutionReview` | `rag.entity_resolution.review` | review hraničních merge |
| `RagEvalRead` / `RagEvalManage` | `rag.eval.read` / `rag.eval.manage` | eval |
| `RagCostRead` | `rag.cost.read` | cost dashboard |
| `RagAbuseRead` | `rag.abuse.read` | abuse |
| `RagManage` | `rag.manage` | admin modulu |

Dotted stringy slouží i jako errorCode/claim; PascalCase const je API. **Drift** (`hybridrag.tenant_search`,
`*.manage_collection`, `RagTenantSearchAllUsers`, `RagSearchTenantAll`, `KnowledgeSearch`) → mapuj na tabulku výše.

## 7. Error codes (jeden each, do `SharedResource.resx` en+cs)

`rag.graph.hops_out_of_range` (NE `rag.hops_out_of_range`/`rag.graph_hops_exceeded`), `rag.lexical.*` dle oblasti 06,
`rag.quota_exceeded`, `rag.embed.unavailable`, `rag.no_relevant_context`. Prefix vždy `rag.`.

## 8. Degraded/partial kontrakt — model z oblasti 17 napříč 13/14/16

`RetrievalStatus { Complete | Partial | Degraded }` + `legs[] { source, ok, latency, reason }`. Zero-retrieval =
`Partial` s `reason = no_relevant_context` (JEDNA reprezentace; NE `noContext`/`degradedReason`/`Degraded=false`).

## 9. Config klíče — kanonický registr v oblasti 24

**Autoritativní zdroj všech tunable knobů = `RagParameterRegistry` / tabulka v [oblasti 24](24-configuration-tuning.md)
(UC-24-01).** Tam je každý knob s kanonickým klíčem, scope (Global·Tenant·Collection·Query), defaultem, runtime-vs-restart,
validačním rozsahem a zda je trasovaný. Namespace tunable knobů = `Rag:*` (bind do `RagTuningOptions` pod `Modules:HybridRag`).
Vyřešený drift (UC-24-16): **`Rag:Fusion:Rrf:K`** (NE `Rag:Rrf:K`/`Rag:Fusion:K`), **`Rag:Chunk:Size`/`:Overlap`**
(NE `Rag:Chunking:*`), **`Rag:Fusion:{CandidateK,TopN}`** (NE `Rag:Hybrid:CandidateK`), **`Rag:Dense:MinSimilarity`**
+ **`Rag:Fusion:MinScore`** (NE `Rag:Retrieval:MinScore`/`Rag:MinSimilarity`), **`Rag:Embedding:Model`** (NE `Rag:Embed:Model`/
`Rag:Embeddings:CurrentModel`), per-leg timeouty `Rag:{Lexical,Rerank}:TimeoutMs` + globální `Rag:OverallTimeout`.
Secrets (`Rag:OpenAi:ApiKey`, `Rag:Rerank:Cohere:ApiKey`) jsou MIMO tunable registr (oddělené Options, fail-fast, maskované
v effective-config). Cron pod `Modules:HybridRag:Jobs:*`. Per-tenant/collection override = DB entita `RagSetting`.

## 10. Cost — jeden autoritativní zdroj

**Real-time enforcement = oblast 22** (Redis token/cost bucket per tenant, per-call). Oblast 19 `rag_usage_daily`
rollup je **jen reporting/lagging**, NEenforcuje (cron flip lagne o interval). Jeden zdroj pravdy pro „překročil kvótu" =
Redis bucket (22), ne denní rollup (19).

## 11. RLS pro dvouvrstvý Scope (custom policy, ne stock `IUserOwned`)

`Chunk`/`Document`/`GraphNode`/`GraphEdge` mají `Scope ∈ {Tenant,User}` + `OwnerUserId (Guid?)`. Stock `IUserOwned`→per-user
RLS (keyed `app.principal_id == UserId`) **NELZE** — skryl by sdílené `Scope=Tenant` řádky. Proto **custom RLS policy**:
`tenant_id = current_tenant AND (scope = 'Tenant' OR owner_user_id = current_principal)`. Sloupec je `OwnerUserId`
(ne zákonem požadovaný `UserId`); auto-konvence `IUserOwned` se NEAPLIKUJE — policy je ručně bootstrapnutá rozšířením
`RlsBootstrapper`. Firemní (tenant-wide) read větev policy viz Open #3.

---

## 12. ⚠️ OTEVŘENÁ ROZHODNUTÍ (§11 — STOP, neimprovizovat; rozhodnout PŘED implementací)

1. **PII × plaintext lexikální/graf index (KRITICKÉ, P0).** `Chunk.Content` je `[Encrypted][PersonalData]`, ale
   pravé BM25 (`pg_search` `@@@`) i `ts_rank`/`tsvector` GIN i grafové lookup klíče (`GraphNode.CanonicalKey`,
   `EntityAlias.NormalizedKey`, `NameEmbedding`) potřebují **plaintext** → po GDPR crypto-shred (DEK) zůstanou PII lexémy
   čitelné v invertovaném indexu / graf klíčích → **erasure díra**, re-identifikace. Možnosti: (a) lexikál+graf jen pro
   ne-PII collections; (b) erasure fyzicky maže celé řádky chunků+uzlů (vč. `search_vector`/BM25 indexu) u user-scope dat
   — pak crypto-shred není jediný mechanismus a musí být zdokumentováno; (c) akceptovat plaintext index s oslabenou zárukou.
   (`EC-02-06-06`, `EC-06-09-05`, `EC-16-07-08`, `UC-20-12`, note 09/14/16/26/33)
2. **Tenant-level encryption key (KEK/KMS).** Šifrování `Scope=Tenant` chunků by chtělo tenant-level DEK, ale platformní
   crypto-shred (`subject_keys`) je výhradně **per-USER**; KEK/KMS je v CLAUDE.md NOT YET. Tenant DEK infra není postavená.
   (`EC-20-04-01`, note 30)
3. **Company-read (tenant-wide) RLS path.** Oblast 00 (UC-00-12) ho prezentuje jako navržený (`is_company_reader` GUC /
   elevated RLS větev); oblast 05 (UC-05-03) ho označuje jako NEROZHODNUTÝ seam. Rozhodnout: buď doplnit do
   `docs/multitenancy-and-infra.md` jako navržený, nebo ho 00 nesmí prezentovat jako hotový. (note 07)
4. **Druhý rerank provider.** Frozen = Cohere `rerank-3.5`. UC-08-09 zavádí self-hosted `bge` switch
   (`Rag:Rerank:Provider`). Buď potvrdit `IRerankGateway` port jako akceptované rozšíření (jako `IStripeGateway` shape),
   nebo flagnout jako §11 (analogie LemonSqueezy druhý payment provider). (note 15)
5. **Základní search permission.** Oblasti 13/16 říkají „žádná speciální permission pro vlastní/tenant search"; oblasti
   15-01/15-12 vyžadují `RagSearch`/`KnowledgeSearch`. Rozhodnout, zda základní search permission existuje. (note 17)

---

## 13. Technické upřesnění (ne open decision, jen korekce katalogu)

- **HNSW pre-filtr** (EC-16-01-02): filtr neběží striktně „PŘED ANN" — u HNSW je během/po index scanu. Bezpečnost (0 leak)
  drží přes RLS + `WHERE` v dotazu; recall vyžaduje `iterative scan` / over-fetch (`hnsw.iterative_scan`, EC-05/16). Pre-filtr
  predikát je vždy součást dotazu, nikdy post-hoc v C#.
- **Index typy nezaměňovat** (note 33): `tsvector` GIN ≠ pg_search BM25 ≠ pgvector HNSW — tři samostatné struktury; erasure/
  delete cesta musí adresovat všechny tři explicitně.
- **Permission claims jsou token-snapshot** (note 24): revoke se projeví až při refresh/expiraci; MCP per-call DB check je
  výjimka (EC-15-06-05), ne default.
