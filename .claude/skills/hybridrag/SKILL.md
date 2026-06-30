---
name: hybridrag
description: Use when implementing, extending, or reasoning about the HybridRag module (hybrid vector + knowledge-graph RAG) in ModularPlatform — ingestion/upload, chunking, embeddings, dense/BM25/RRF retrieval, reranking, knowledge graph, answer synthesis + citations, MCP, evaluation (golden set/rules/online), human-in-the-loop, model management + cost optimization, config/tuning, audit, observability, and the RAG UI. Loads the frozen decisions, the canonical naming source (CONVENTIONS.md), the UC/EC catalog location, the 5 open blockers, and the reuse seams so nothing is reinvented. Triggers on "RAG modul", "hybrid rag", "HybridRag", "knowledge base modul", "vector search modul", "RAG ingest/retrieval/eval/HITL".
---

# HybridRag — implementační rozcestník

Nový modul `HybridRag` = produkčně robustní hybrid vektor + knowledge-graph RAG nad ModularPlatform. **UC/EC katalog
je hotový (design docs), kód zatím NEexistuje.** Implementace = podle katalogu, gated na otevřených rozhodnutích (§4).

## 0. PŘEČTI NEJDŘÍV (pořadí)
1. **`docs/hybrid-rag/CONVENTIONS.md`** — ZDROJ PRAVDY pro pojmenování (route prefix, entity, tabulky, permissions,
   config klíče, enum hodnoty) + konsolidovaná **otevřená rozhodnutí §12**. Katalog generovali paralelní agenti → místy
   drift; **kde se soubor rozchází, vyhrává CONVENTIONS.**
2. **`docs/hybrid-rag/README.md`** — index: ⛔ PREREKVIZITA [`0-core-ai-gateway.md`] + 33 oblastí (00–32), roll-up (**430 UC / 2613 EC** — vč. core prereq + PDF-audit doplňků).
3. **Relevantní `docs/hybrid-rag/NN-*.md`** pro oblast, kterou děláš (UC-NN-MM + EC-NN-MM-KK).
4. **Platform kanon** (CLAUDE.md §2): kopíruj Identity/Files/Billing/Operations slice — NEvymýšlej paralelní flow.
5. Plán: `~/.claude/plans/pojdme-udelat-plan-na-moonlit-kahan.md` (build roadmap fáze 0–5).

## 1. Frozen rozhodnutí (NEpřehodnocovat bez usera)
- **Graf = relační edge tabulky** (`GraphNode`/`GraphEdge`) + pgvector přes **EF/LINQ** (NE Apache AGE/Neo4j → ty by chtěly
  raw Cypher a obešly RLS/audit/xmin). 1–2 hop = LINQ join. Community detection (Leiden) = offline batch job.
- **Korpus dvouvrstvý:** `Scope = Tenant | User`. User hledá v tenant korpusu + svých privátních současně; tenant-admin
  (permission-gated) napříč všemi usery. RLS = **custom policy** `tenant_id AND (scope='Tenant' OR owner_user_id=principal)`
  (NE stock `IUserOwned` — skryl by sdílené tenant řádky).
- **Lexikál = pravé BM25 přes ParadeDB `pg_search`** (`@@@`) by default; `ts_rank_cd` LINQ fallback (`Rag:Lexical:Provider`).
  BM25 nemá LINQ surface → **úzká raw-SQL carve-out** (parametrizovaný `FromSqlInterpolated`, injection-safe). Spolu s
  pgvector-extension DDL a custom-RLS-policy = jediné 3 povolené raw-SQL ostrůvky (jinak EF/LINQ only).
- **Vektor** = pgvector HNSW, `CosineDistance` LINQ, sloupec **`halfvec(3072)`** (HNSW limit 2000 dim pro `vector`).
- **RRF k=60 v C#** (fúze přes RANK, NE raw skóre). **Rerank** = Cohere `rerank-3.5`. **Embed** = OpenAI `text-embedding-3-large`.
  **Chat/synthesis** = Claude (Anthropic.SDK). **Fake gateways pod `Rag:UseFakeGateways`** (vzor `FakeStripeGateway`).
- **Porty** (anti-corruption, mirror `IStripeGateway`): `ILlmGateway` (jeden chokepoint pro VŠECHNA model volání — routing,
  fallback, cache, usage capture), `IEvaluator` (Deterministic → RagMetric → LlmJudge), `IEmbeddingGenerator`/rerank.
- **Fine-tuning = NE na znalost** (PDF §7, CONVENTIONS §18): znalost→RAG (čerstvá/citovatelná/mazatelná crypto-shred), chování→prompt. Nefine-tunovat firemní dokumenty (erasure díra + rozbije citace). Hierarchy prompt→few-shot→RAG→fine-tune.
- **Identity z tokenu** (`ITenantContext.UserId`), NIKDY z route/body/LLM argumentu. Vše UTC. Outbox commit =
  `SaveChangesAndFlushMessagesAsync`. Idempotency = UNIQUE + catch `DbUpdateException`. Handlery idempotentní + order-independent.

## 2. Durable orchestrace = WOLVERINE, NE Temporal (důležité — ať se nezapomene)
**Nepřidáváme Temporal ani jiný job-engine.** Core už durable orchestraci MÁ přes **Wolverine** (`ModularPlatform.Messaging`,
`WolverineFx`+`.Postgresql`+`.EntityFrameworkCore` pinned). Wolverine saga + outbox/inbox = ~70 % Temporalu na Postgresu,
bez nové infry; CLAUDE.md §6 zakazuje nový queue/outbox/job-engine.
- **Ingest pipeline** (extract→chunk→contextualize→embed→index→graf) = **Wolverine saga** v module DbContextu — kopie
  `CreditPurchaseSaga.cs:30`. Per-stage idempotency key (`embed:{docId}:{ingestRunId}:{chunkHash}`).
- **HITL blocking approval gate** (oblast 32) = **Wolverine saga čekající na human-decision message** (jako saga čeká na
  timeout/confirm) → deterministický resume po hodinách/restartu. To je ten „durable wait", co lidi řeší Temporalem — u nás
  Wolverine v core. Status pro caller/UI = `IOperationStore` 202.

## 3. Reuse seamy (kopíruj, NEvymýšlej) — file:line
| Potřeba | Reuse | Kde |
|---|---|---|
| Vertical slice (Command/Validator/Handler/Endpoint) | `Features/Users/RegisterUser/*` | Identity |
| Read query | `GetProfileHandler.cs:12` (`IReadDbContextFactory`) | Identity |
| Outbox publish + commit | `RegisterUserHandler.cs:22` (`IDbContextOutbox`, `SaveChangesAndFlushMessagesAsync`) | Identity |
| Worker handler shell | `ProvisionCreditAccountHandler.cs:13` (public, `Handle(evt, IDispatcher, ct)`) | Billing |
| Durable multi-step pipeline + HITL gate | `CreditPurchaseSaga.cs:30` (Wolverine EF saga) | Billing |
| Long work 202 + status | `StartDemoOperationHandler.cs:17` + `IOperationStore` | Operations |
| LLM chat / agentic tool-use | `ClaudeVibeAgentGateway.cs:85` (`IChatClient` + Anthropic.SDK) | Marketing |
| Streaming SSE (delta/done, disconnect-safe) | `StreamMessageEndpoint.cs:34` | Marketing |
| Blob bytes + metadata split, compensating delete | `IFileStorage` (`Ports.cs:166`) + `UploadFileHandler.cs:21` | Files |
| Realtime push (in-app feed) | `IRealtimePublisher.PublishToUserAsync` (`Ports.cs:98`) | Realtime BB |
| Provider port + fake-under-flag | `IStripeGateway` + `MarketingModule.cs:51` (`UseFakeGateways`) | Billing/Marketing |
| Custom metriky | `PlatformMetrics.Meter` (`PlatformMetrics.cs:19`, `platform.rag.*`) | Telemetry BB |
| Audit (changed fields → JSONB) | `AuditInterceptor` → `hybridrag_audit_entries`; PII = `[PersonalData]`+`IDataSubject` crypto-shred | Persistence BB |

## 4. ⚠️ 5 OTEVŘENÝCH ROZHODNUTÍ (§11 STOP — vyřeš s userem PŘED dotčenou oblastí)
1. **PII × plaintext lexikální/graf index (KRITICKÉ):** `Chunk.Content` `[Encrypted]` + graf lookup klíče potřebují plaintext
   pro BM25/tsvector/lookup → po crypto-shred zůstanou PII tokeny v indexu → **GDPR erasure díra**. (oblasti 06/16/20)
2. **Tenant-level encryption key (KEK/KMS)** — crypto-shred je per-USER; tenant DEK infra není (NOT YET).
3. **Company-read (tenant-wide) RLS path** — oblast 00 ho má za hotový, oblast 05 za nerozhodnutý.
4. **Druhý rerank provider** (bge self-hosted) — `IRerankGateway` rozšíření vs §11.
5. **Základní search permission** — existuje (15) vs neexistuje (13/16)?
6. **CORE `ModularPlatform.Ai`: building-block vs always-on modul** (UC-CORE-17) — určuje reference graf/migrace/DI.
7. **Ledger write fail → reconciliation** (EC-CORE-04-02) · 8. **Redis budget down: fail-open vs fail-closed** (EC-CORE-06-02).

## 5. Build pořadí (fáze) a registrace
**Fáze −1 (PREREKVIZITA — PŘED modulem):** core building-block `ModularPlatform.Ai` (LLM gateway + cost/budget/cache/model-registry) — viz [`docs/hybrid-rag/0-core-ai-gateway.md`](../../docs/hybrid-rag/0-core-ai-gateway.md) (18 UC / 122 EC). Marketing přemigrovat (UC-CORE-18).
**Fáze 0** infra+tenancy (trio, 4-host registrace, pgvector extension, custom RLS policy, entity model, `KnowledgeCollection`
CRUD, cross-isolation test) → **1** ingest/chunk/embed (Wolverine saga) → **2** hybrid retrieval+rerank → **3** graf+entity
resolution → **4** answer+MCP → **5** eval/HITL/model-cost/degradace/observ. Pak UI (oblasti 26–29).

**Registrace modulu (8 míst):** `Api/Program.cs`, `Worker/WorkerHostBuilder.cs`, `Jobs/JobsHostBuilder.cs`,
`MigrationService/MigrationHostBuilder.cs`; `appsettings.json` `Modules:HybridRag:Enabled=true`; `ArchitectureTests`
(ModuleCoreAssemblies + LoadAssemblies); `Hosts.Tests` BootArgs; `MessageWireIdentityTests` FrozenWireNames (každý nový event).
+ errorCodes do `SharedResource.resx` (en+cs). Testy na sdíleném `PlatformApiFactory` (Testcontainers + **pgvector image**).

## 6. Kanonické pojmenování (detail v CONVENTIONS.md)
Modul `HybridRag` · route `/v1/hybridrag/...` (drift `/v1/rag/` → čti jako hybridrag) · tabulky `hybridrag_*` ·
`HybridRagDbContext` · permissions `PlatformPermissions.Rag*` + dotted `rag.*` · config namespace `Rag:*` (registr oblast 24,
override `RagSetting`) · `RetrievalStatus{Complete|Partial|Degraded}` · entity: KnowledgeCollection, Document, Chunk, GraphNode,
GraphEdge, EntityAlias, IngestSaga, RagConversation/Turn/AnswerCitation/Trace, RagSetting, RagModel, RagUsageLedger,
RagDataset/Version/Item, RagReviewItem, RagReviewPolicy.

## 7. Knihovny (free + battle-tested — CONVENTIONS §16)
Přidat: **PdfPig** (PDF) · **DocumentFormat.OpenXml** (DOCX) · **AngleSharp** (HTML) · **Markdig** (MD) ·
**Microsoft.ML.Tokenizers** (token count) · **HtmlSanitizer/Ganss.Xss** (ingest XSS) · **F23.StringSimilarity** (entity-res) ·
**JsonSchema.Net** (structured-output validace) · **Polly** (resilience) · **Microsoft.Extensions.AI.Evaluation** (RAG
evaluátory → `IEvaluator`) · FE **DOMPurify** (XSS). Optional: QuikGraph · graspologic (offline Leiden) · promptfoo · LiteLLM (infra).
NEpřidávat (máme): Pgvector.EFCore, pg_search, Wolverine, MEAI+Anthropic.SDK, MCP SDK, Redis, Quartz, RateLimiter, Argon2, S3.

## 8. Core vs modul (CONVENTIONS §17 — pravidlo ≥2 moduly → building-block)
- **Plný UC/EC = [`0-core-ai-gateway.md`](../../docs/hybrid-rag/0-core-ai-gateway.md) (PREREKVIZITA).** ⚠️ Otevřená sub-decision (UC-CORE-17): čistý building-block s perzistencí vs always-on platform modul — rozhodnout PŘED stavbou.
- **DO CORE (nový building-block `ModularPlatform.Ai`):** LLM gateway + cost/usage/budget. `ILlmGateway`+`IEmbeddingGenerator`,
  **`AiUsageLedger`** (platform-wide, NE `Rag*`), budget enforce, cache, model registry+pricing, tokenizer, JSON-schema validace.
  Důvod: Marketing už LLM volá (2. konzument) + cost platform-wide + „jediný chokepoint". **Oblast 30 = core**, RAG konzumuje;
  Marketing přemigrovat. 
- **CORE port, impl module-first:** `IEvaluator` (+ Microsoft.Extensions.AI.Evaluation) — RAG první konzument, promote později.
- **MODUL (RAG):** doc parsing (`IDocumentTextExtractor`, promote jen při 2. konzumentovi), retrieval/graf/chunking/entity-res/
  citace/HITL/datasety/UI. Polly/HtmlSanitizer = knihovní ref, ne building-block.

## 9. Kritické gotchas z PDF auditu (NEopakovat tyto chyby — ověřeno proti příručce)
- **Prompt cache wire-order:** prompt se renderuje **`tools (pozice 0) → system → retrieved-docs → volatilní`** (NE system před tools!). Breakpointy od nejvíc upstream (tools); změna tool setu busti vše následující. Min-prefix **per-model** (Opus 4.8 = 4096 tok., Sonnet 4.6 = 2048). Assistant-prefill pro JSON je **mrtvý → 400**, použij `output_config.format`. **20-block lookback** → rolling intermediate breakpoint ~á15 bloků. Mid-conversation závazná instrukce = `{role:system}` do `messages[]` (NE editovat top-level system = busted cache; NE do user/tool textu = spoofovatelné). `strict:true` = **top-level pole tool definice** (ne `tool_choice`). (oblast 14, 0-core UC-CORE-14)
- **Multitenant izolace/audit:** **`SET LOCAL` ne `SET`** (GUC `app.tenant_id`/`app.principal_id` transaction-scoped, jinak přeteče přes pooled connection = leak). **RLS `ENABLE` + `FORCE`** (jinak app-owner role obejde policy). **PromptVersion/ConfigVersion** na `AiUsageLedger` + cost report (atribuce „v2 promptu +30 %"). (oblast 16, 19, 0-core)
- **Durable (Wolverine):** saga/stage zprávy nesou **VÝHRADNĚ ID/reference** (DocumentId, IngestRunId, StorageKey), NIKDY chunk content/embeddings/text (fat envelope). Per-call timeout na každé LLM/embed volání. Cost-odstupňovaný retry (drahý stage míň pokusů). Stage versioning = additive-only + frozen wire names. (oblast 04)
- **MCP:** server **NEexponuje sampling ani elicitation** (least-privilege, capability se nedeklaruje). `description` **preskriptivní o triggeru** („Call this when…"), ne jen co tool dělá. Parallel `tool_result` v **JEDNÉ** user zprávě + `tool_use_id` matching. (oblast 15)
- **Retrieval:** chunk `MaxTokens` ≤ ~1024 (doporučeno 512) — velký chunk = embedding averaging do mdlého středu. Seed/entry-point lookup (`NormalizedKey`/`CanonicalKey`) **MUSÍ mít index** (GIN/btree), jinak seq scan na horké cestě. (oblast 02, 11, 12)

## 10. N/A — vědomě se NEvztahuje (NEpřidávat, ověřeno auditem)
- **Fine-tuning mechanika** (LoRA/QLoRA, catastrophic forgetting, overfitting) — **nefine-tunujeme** (znalost→RAG; stance CONVENTIONS §18). N/A je mechanika, ne rozhodnutí.
- **Temporal-replay pasti** (`DateTime.Now`/`Guid`/`Random` ve workflow rozbije replay, heartbeat, `GetVersion`) — **Wolverine nereplayuje**, každý handler = čerstvá exekuce. Ekvivalent (idempotence, fat-state→ref, additive versioning) podchycen jinak.
- **MCP stdio stdout-discipline** — používáme **Streamable HTTP** (multi-tenant + OAuth), ne stdio.

## Kdy použít tento skill
Vždy když pracuješ na čemkoli v modulu HybridRag (implementace slice, nová oblast, review, rozhodnutí). Začni krokem 0
(přečti CONVENTIONS + relevantní oblast), respektuj frozen rozhodnutí (krok 1–2), reuse seamy (krok 3), a u dotčené oblasti
ověř, zda nenarazíš na otevřený blocker (krok 4) — pokud ano, ZASTAV a zeptej se usera.
