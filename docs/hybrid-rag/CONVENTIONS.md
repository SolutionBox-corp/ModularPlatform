# HybridRag — kanonické konvence (zdroj pravdy)

UC/EC katalog (oblasti 00–32) generovaly nezávislé agenti paralelně, takže jednotlivé soubory místy
**driftují v pojmenování** (route prefix, názvy entit/tabulek, permission konstanty, enum hodnoty). Tento soubor
**zmrazuje kanonickou volbu** — při implementaci platí TOHLE, ne zdrojový drift v konkrétním souboru. Sjednocení nálezů
z completeness-critic passu + gap/konsolidačního passu. **Kde se katalog rozchází, vyhrává tento dokument.**

> **Route prefix drift (časté):** mnoho souborů píše `/v1/rag/...`. Kanonicky je **`/v1/hybridrag/...`** (viz §2).
> Čti každý `/v1/rag/...` jako `/v1/hybridrag/...`.

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
**Model/cost (oblast 30):** `RagModel` (registry: name, provider, kind chat|embed|rerank, pricing, status), `RagUsageLedger`
(append-only per-LLM-volání: tenant/user/feature/model, 4 token countery prompt/completion/cache_creation/cache_read, USD, cache-hit).
Všechna model volání jdou přes **`ILlmGateway` port** (jeden chokepoint, fake pod `Rag:UseFakeGateways`) — žádná feature nevolá provider přímo.
**Eval (oblast 18/31):** `RagDataset`/`RagDatasetVersion` (append-only, immutable)/`RagDatasetItem` (input+expected-context+expected-output);
score přes **`IEvaluator` port** (Deterministic → RagMetric → LlmJudge, v tomto pořadí). Eval run = 202 long-running operace.
**HITL (oblast 32):** `RagReviewItem` (type entity_merge|answer_approval|ingest_approval|feedback_label, status Pending/Reserved/Decided,
reservation TTL), `RagReviewPolicy` (declarative per-tenant: thresholdy three-band auto-serve/flag/abstain, action classes, sampling %, reviewer role).
**Sjednocení (gap pass):** review fronty entity-res (UC-11-05) i golden harvest (UC-18-07) → JEDNA abstrakce `RagReviewItem` (oblast 32 je autoritativní).

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

**Real-time enforcement = oblast 22** (Redis token/cost bucket per tenant, per-call) + **`ILlmGateway` budget guard (oblast 30)**
→ 429 + errorCode `rag.quota_exceeded`. Oblast 19 `rag_usage_daily` rollup + UC-19-10 budget-alert jsou **jen reporting/alerting/lagging**,
NEenforcují (cron lagne o interval). Autoritativní „překročil kvótu" = Redis bucket (22)/gateway (30), ne denní rollup (19).
Per-LLM-volání cost se zapisuje do **`RagUsageLedger`** přes `ILlmGateway` (oblast 30) — jeden zdroj nákladů; oblast 19 nad ním reportuje.

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

---

## 14. Konsolidace (gap pass) — sjednocené průřezové vzory

- **Per-query persistence — jeden lifecycle.** `rag_query_trace` (19 diagnostika), `eval_samples` (18/31 online eval),
  trace effective-config stamp (24-12) a operation audit (25) řeší tentýž „per-query record". Sjednotit schéma + retention +
  PII-shred do jednoho modelu (nebo explicitně zdokumentovat dělbu), aby nevznikaly duplicitní zápisy a divergentní GDPR erasure.
- **Společný „admin read slice" pattern.** Dashboard/read endpointy (18-08 eval, 19-06 cost, 19-07 explain, 24-03 effective-config,
  25 audit, 23 list/detail, 28 UI) sdílejí: RLS tenant-scope, IDOR→404, range-cap proti DoS, PII-redakce v agregátu, permission gate.
  Vyextrahovat jeden pattern; **UI oblasti 26–29 ho jen KONZUMUJÍ, neredefinují** business logiku/izolaci.
- **Admin/dashboard routes (kanonicky pod `/v1/hybridrag/`):** `/admin/cost`, `/admin/queries/{id}/explain`,
  `/admin/config/{registry,effective,tenant,collection}`, `/admin/audit`, `/eval/summary`, `/eval/datasets`, `/eval/runs`,
  `/models`, `/models/compare`, `/reviews`, `/reviews/{id}/decision`, `/query/{id}/feedback`. (Drift `/v1/rag/...` → čti jako `/v1/hybridrag/...`.)
- **UI ↔ backend.** Frontend (26–29) = tenký konzument přes BFF (Model A, token jen server-side) + TanStack Query (jeden data source) +
  jeden SSE provider. Validace/izolace/identita je VŽDY backend; UI prvky se jen skrývají/disablují dle permission (autorita = backend → 403/404).

---

## 15. Durable orchestrace = Wolverine, NE Temporal (frozen)

**Nepřidáváme Temporal .NET SDK ani jiný job-engine.** Core už durable orchestraci MÁ přes **Wolverine**
(`ModularPlatform.Messaging`; `WolverineFx`+`.Postgresql`+`.EntityFrameworkCore` pinned v `Directory.Packages.props`).
Wolverine saga + outbox/inbox = ~70 % Temporalu na Postgresu, bez nové infry; CLAUDE.md §6 zakazuje nový queue/outbox/job-engine.
- **Ingest pipeline** (oblast 04) = Wolverine saga v module DbContextu, kopie `CreditPurchaseSaga.cs:30`.
- **HITL blocking approval gate** (oblast 32) = Wolverine saga čekající na human-decision message → deterministický resume
  po hodinách/restartu (durable wait, co jiní řeší Temporalem). Status pro caller/UI = `IOperationStore` 202.

→ Durable orchestrace zůstává v **core (Wolverine)**; RAG modul ji jen POUŽÍVÁ (saga v module DbContextu, jako Billing).

---

## 16. Knihovny (free + battle-tested + reálně pomáhají)

Pravidlo CLAUDE.md „battle-tested před vlastní implementací". Přidat do `Directory.Packages.props` (CPM):

| Balíček | Licence | Co řeší | Oblast |
|---|---|---|---|
| **UglyToad.PdfPig** | Apache-2.0 | extrakce textu/layoutu z PDF | 01 |
| **DocumentFormat.OpenXml** | MIT | DOCX/XLSX/PPTX extrakce | 01 |
| **AngleSharp** | MIT | HTML parse + bezpečná extrakce textu | 01 |
| **Markdig** | BSD-2 | Markdown parse | 01/02 |
| **Microsoft.ML.Tokenizers** | MIT | token counting (chunking, context budget, cost) | 02/30 |
| **HtmlSanitizer** (Ganss.Xss) | MIT | sanitizace ingestovaného HTML (injection) | 20 |
| **F23.StringSimilarity** | MIT | Jaro-Winkler/Levenshtein pro entity resolution | 11 |
| **JsonSchema.Net** (json-everything) | MIT | validace LLM structured-output / tool args | 13/15/31 |
| **Polly** (nebo `Microsoft.Extensions.Resilience` MIT) | BSD-3 | per-leg timeout/retry/circuit-breaker/fallback | 17 |
| **Microsoft.Extensions.AI.Evaluation** (`.Quality`/`.Safety`/`.Reporting`) | MIT | RAG evaluátory (groundedness/relevance/retrieval) → `IEvaluator` | 18/31 |
| FE: **DOMPurify** | Apache-2.0 / MPL-2.0 | sanitizace renderovaného markdownu/citací (XSS) | 26/27 |

**Optional (až když potřeba):** QuikGraph (MS-PL, graf algo PageRank/components — 12) · graspologic (MIT, offline Leiden community
detection — 12) · promptfoo (MIT, Node, CI eval brána — 18/31) · LiteLLM/OpenRouter (infra proxy, ne lib — breadth providerů, 30).

**NEpřidávat (máme / vlastní je správně):** Pgvector.EFCore (vektor), pg_search (BM25), Wolverine (durable), MEAI+Anthropic.SDK
(LLM klient), MCP C# SDK, MEAI builder (cache/telemetry/tool-loop), StackExchange.Redis, Quartz, ASP.NET RateLimiter, Argon2, AWSSDK.S3.

## 17. Core (building-block) vs modul — co kam patří

> **Plný UC/EC: [`0-core-ai-gateway.md`](0-core-ai-gateway.md) (PREREKVIZITA, 18 UC / 122 EC) — postavit PŘED modulem.**
> ⚠️ **Otevřená sub-decision (UC-CORE-17, §11 STOP):** `ModularPlatform.Ai` = čistý building-block s perzistencí
> (`src/building-blocks/`, žádný `IModule`) NEBO tenký **always-on platform modul** (`src/modules/`, `IModule`, vždy Enabled)?
> Pro modul mluví: nese entity + migrace + admin endpointy + GDPR eraser. Pro building-block: musí být volán Z modulů
> (modul→modul Core je zakázán reference grafem). Určuje reference graf/migrace/DI — rozhodnout PŘED stavbou.

Pravidlo CLAUDE.md §3: shared mechanismus, který potřebují **≥2 moduly**, → **building-block + port**, ne per-modul.

**→ DO CORE (nový building-block `ModularPlatform.Ai`):** LLM gateway + cost/usage/budget vrstva.
`ILlmGateway` (chat) + `IEmbeddingGenerator` (embed) wiring · **`AiUsageLedger`** (append-only, 4 token countery, USD —
PLATFORM-wide, NE `Rag*`) · per-tenant budget enforcement (429) · cache (exact/semantic/prefix) · model registry + pricing ·
`Microsoft.ML.Tokenizers` · `JsonSchema.Net` (structured-output validace). **Důvod:** Marketing už LLM volá
(`IVibeAgentGateway`) = 2. konzument; cost musí být platform-wide (per-tenant napříč VŠÍM LLM použitím, ne jen RAG); „jediný
chokepoint" funguje jen v core. **Oblast 30 = tento core building-block**, RAG ho jen konzumuje; Marketing přemigrovat na něj.

**→ CORE port, impl module-first:** `IEvaluator` (+ `Microsoft.Extensions.AI.Evaluation`). Eval je cross-module, ale RAG je
první konzument → port v core / impl v RAG modulu, **promote až přijde 2. konzument** (Marketing chat eval).

**→ MODUL (RAG doména):** doc parsing (port `IDocumentTextExtractor` — promote do core/Files jen při 2. konzumentovi),
vektor/BM25/RRF/rerank/chunking/graf/entity-res (`F23.StringSimilarity`)/citace/HITL fronty/golden-set datasety/RAG-UI
(`DOMPurify`). `Polly`/`HtmlSanitizer` = knihovní ref použité kde třeba, ne samostatný building-block.


---

## Doplňky / Opravy z PDF audit (PDF §5 Durable orchestration)

Doplňky k **§15 (Durable orchestrace = Wolverine, NE Temporal)**:

- **Reference-only saga/stage zprávy (fat-state zákaz).** Ingest saga (oblast 04) a všechny její stage zprávy nesou VÝHRADNĚ identifikátory/reference (`Id`/sagaId, `DocumentId`, `IngestRunId`, `StorageKey`, `CollectionId`, `Scope`, `OwnerUserId`) — NIKDY chunk content, extrahovaný text ani embeddingy. Wolverine durable envelope se serializuje a perzistuje do Postgresu (`wolverine_incoming/outgoing_envelopes`), takže fat payload = bobtnání durable store, PII v durable frontě mimo `[Encrypted]` at-rest ochranu chunků a delší PII expozice v DLQ. Handler si data vždy načte z DB / `IFileStorage` podle Id. Tvrdý strop stage zprávy ~256 KB (překročení = code-smell/fail). (EC-04-01-11/12)
- **Wolverine ≠ Temporal-style deterministický replay.** Wolverine NErobí event-sourced re-přehrání JEDNOHO workflow z zaznamenané historie — každý handler/stage je ČERSTVÁ exekuce, ne replay. Proto se Temporal past „`DateTime.Now`/`Guid.NewGuid()`/`Random` ve workflow rozbije deterministickou historii" na Wolverine sagu NEVZTAHUJE; nedeterministická volání uvnitř handleru jsou v pořádku. ALE: handler re-doručený/retryovaný po pádu může vzít JINOU časovou/logickou větev než původní pokus (`IClock.UtcNow` se posune, DB stav je jiný) → proto musí být handlery **commutative/idempotentní a order-independent** (UNIQUE klíče + catch `DbUpdateException`, Status guard, refetch live state); spoléhání na „stejný výsledek při re-doručení" je chyba (CLAUDE.md §9b race-defence vrstvy).

---

## 18. Fine-tuning: NE na znalost (frozen — PDF §7)

Decision hierarchy: **prompt → few-shot → RAG/tool-use → fine-tune**; vrstvu výš zkus až když nižší **měřitelně** selhala (na eval setu, ne pocitově).
**HybridRag NEfine-tunuje na znalosti** — *„For knowledge you retrieve, you don't bake it in."* Znalost žije venku v RAG: čerstvá,
citovatelná přes `RagAnswerCitation`, mazatelná per-GDPR (crypto-shred). Fine-tuning na firemní dokumenty = (a) **crypto-shred/erasure
díra** (znalost zapečená do vah nejde smazat → GDPR), (b) **rozbíjí citace** (model neumí říct zdroj), (c) zastará dnem tréninku +
maintenance dluh (verzování/retrain/drift). Klasifikace/routing/entity-res = prompt/heuristika (flexibilní, bez tréninkového dluhu),
ne fine-tuned klasifikátor. **Kdyby** fine-tune byl kdy nutný — JEN na **formu/styl/voice ve velkém objemu** (amortizace), pak
LoRA/PEFT (zmrazený base, ne full fine-tune) + eval proti prompt-only baseline; ale nejdřív few-shot. Cost řešíme routing+cache
(oblast 30/14), ne fine-tune.
