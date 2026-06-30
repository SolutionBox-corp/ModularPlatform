# HybridRag — UC/EC katalog

Vyčerpávající číslovaný katalog **use cases** a **edge cases** pro nový modul `HybridRag` (hybrid vektor + knowledge-graph RAG) v ModularPlatform — **produkčně robustní systém**: backend (retrieval, graf, durable ingest, eval, audit, cost) + **frontend UI** (upload, chat, dashboardy, tuning, HITL) + **core LLM gateway prerekvizita**. **Design dokumentace — produkční kód zatím neexistuje**; katalog je zdroj pravdy chování + podklad pro test scaffolding a implementaci. Plán: `~/.claude/plans/pojdme-udelat-plan-na-moonlit-kahan.md`.

> 📐 **[CONVENTIONS.md](CONVENTIONS.md) = nadřazený zdroj pravdy** pro pojmenování + konsolidovaná otevřená rozhodnutí. Kde se soubor rozchází, vyhrává CONVENTIONS.

## ⛔ PREREKVIZITA — postavit PŘED prací na modulu

| Prerekvizita | #UC | #EC | Proč první |
|---|---:|---:|---|
| [⛔ CORE — LLM/AI gateway (`ModularPlatform.Ai`)](0-core-ai-gateway.md) | 18 | 122 | **Core building-block** (ne modul): LLM volá ≥2 moduly (Marketing + RAG) → cost/budget musí být platform-wide, „jediný chokepoint" funguje jen v core. RAG oblast 30 ho konzumuje. Otevřená sub-decision: building-block vs always-on modul (UC-CORE-17). |

> **Pozn.:** `ILlmGateway` + `AiUsageLedger` + budget + cache + model registry + tokenizer + structured-output validace = **core**, ne v RAG modulu (CONVENTIONS §17). Postavit a Marketing na něj přemigrovat (UC-CORE-18) PŘED/SOUBĚŽNĚ s modulem.

## Bloky modulu (00–32)
- **00–09** korpus & ingest & retrieval základ · **10–12** knowledge graph · **13–17** answer/cache/MCP/izolace/degradace
- **18–25** eval · observability+cost · GDPR · streaming · rate-limit · admin · config-registry · audit
- **26–29** UI (upload+kolekce · chat+citace · dashboardy · config+HITL konzole) · **30** model+cost (konzumuje CORE) · **31** eval-deep · **32** HITL

## ⚠️ Otevřená rozhodnutí PŘED implementací (§11 — STOP) — [CONVENTIONS.md §12](CONVENTIONS.md)
1. **PII × plaintext lexikální/graf index (KRITICKÉ)** · 2. **Tenant-level KEK/KMS** · 3. **Company-read RLS path** · 4. **Druhý rerank provider** · 5. **Základní search permission** · 6. **CORE `ModularPlatform.Ai`: building-block vs always-on modul** (UC-CORE-17) · 7. **Ledger write fail reconciliation** (EC-CORE-04-02) · 8. **Redis budget down: fail-open/closed** (EC-CORE-06-02).

## Pokrytí (roll-up) — **prerekvizita (1) + 33 oblastí modulu = 429 UC, 2545 EC**

| # | Oblast | #UC | #EC | P0 | P1 | P2 | P3 |
|---|---|---:|---:|---:|---:|---:|---:|
| [⛔CORE](0-core-ai-gateway.md) | LLM/AI gateway (prerekvizita) | 18 | 122 | 27 | 54 | 35 | 6 |
| [00](00-collections-tenancy.md) | Collections & tenancy | 14 | 106 | 37 | 32 | 28 | 9 |
| [01](01-document-ingestion.md) | Document ingestion (upload & text extraction) | 8 | 59 | 19 | 32 | 8 | 1 |
| [02](02-chunking-contextualization.md) | Chunking & contextualization | 11 | 72 | 32 | 21 | 13 | 6 |
| [03](03-embedding.md) | Embedding generation | 9 | 54 | 12 | 25 | 14 | 3 |
| [04](04-durable-ingest-saga.md) | Durable ingest saga & indexing | 15 | 111 | 33 | 45 | 29 | 4 |
| [05](05-dense-vector-retrieval.md) | Dense vector retrieval (pgvector) | 11 | 60 | 27 | 20 | 12 | 0 |
| [06](06-sparse-lexical-retrieval.md) | Sparse / lexical retrieval (BM25) | 12 | 84 | 19 | 31 | 24 | 10 |
| [07](07-hybrid-fusion-rrf.md) | Hybrid fusion (RRF) | 12 | 57 | 12 | 19 | 15 | 11 |
| [08](08-reranking.md) | Reranking (cross-encoder) | 12 | 55 | 17 | 20 | 17 | 1 |
| [09](09-freshness-versioning.md) | Freshness & versioning | 11 | 71 | 18 | 24 | 25 | 4 |
| [10](10-graph-extraction.md) | Knowledge graph extraction | 12 | 81 | 20 | 32 | 28 | 0 |
| [11](11-entity-resolution.md) | Entity resolution | 12 | 80 | 33 | 27 | 17 | 1 |
| [12](12-graph-retrieval.md) | Graph retrieval (local / global / community) | 11 | 80 | 19 | 39 | 20 | 2 |
| [13](13-answer-synthesis-citations.md) | Answer synthesis & citations | 15 | 84 | 15 | 32 | 27 | 10 |
| [14](14-prompt-caching-context.md) | Prompt caching & context assembly | 8 | 38 | 12 | 14 | 11 | 1 |
| [15](15-mcp-server-tooluse.md) | MCP server & tool-use | 11 | 59 | 21 | 20 | 15 | 3 |
| [16](16-multitenant-isolation-security.md) | Multi-tenant isolation & security | 10 | 73 | 30 | 31 | 11 | 0 |
| [17](17-graceful-degradation.md) | Graceful degradation & resilience | 14 | 80 | 13 | 39 | 24 | 4 |
| [18](18-evaluation.md) | Evaluation (offline gate + online sampling) | 9 | 62 | 25 | 22 | 13 | 2 |
| [19](19-observability-cost.md) | Observability & cost | 12 | 83 | 28 | 27 | 28 | 0 |
| [20](20-gdpr-pii-lifecycle.md) | GDPR / PII / data lifecycle | 11 | 77 | 30 | 30 | 14 | 3 |
| [21](21-streaming-realtime.md) | Streaming & realtime | 9 | 55 | 14 | 18 | 19 | 4 |
| [22](22-rate-limiting-abuse.md) | Rate limiting & abuse | 8 | 54 | 11 | 24 | 17 | 2 |
| [23](23-admin-management.md) | Admin / management (catalogue, reindex, delete) | 12 | 101 | 39 | 32 | 24 | 6 |
| [24](24-configuration-tuning.md) | Configuration, tuning & parameter registry | 16 | 169 | 56 | 77 | 29 | 7 |
| [25](25-audit-history.md) | Audit & change history (config + operace) | 15 | 116 | 33 | 46 | 35 | 2 |
| [26](26-ui-upload-collections.md) | UI — Document upload & collection management | 14 | 49 | 5 | 28 | 20 | 2 |
| [27](27-ui-search-chat-citations.md) | UI — Grounded search, chat & citations | 15 | 60 | 7 | 24 | 24 | 11 |
| [28](28-ui-dashboards.md) | UI — Eval, cost, audit & observability dashboards | 14 | 52 | 12 | 14 | 23 | 3 |
| [29](29-ui-config-hitl.md) | UI — Configuration/tuning panel & HITL review console | 18 | 78 | 7 | 14 | 35 | 22 |
| [30](30-model-cost-optimization.md) | Model management, comparison & cost optimization | 16 | 54 | 15 | 27 | 12 | 0 |
| [31](31-evaluation-deep.md) | LLM evaluation — golden set, rules, online eval, model comparison | 16 | 51 | 8 | 20 | 11 | 3 |
| [32](32-human-in-the-loop.md) | Human-in-the-loop — configurable review, approval & feedback | 18 | 58 | 14 | 31 | 12 | 0 |
| | **CELKEM** | **429** | **2545** | **720** | **991** | **689** | **143** |

> P-rozpad = Severity (EC) + Priorita (UC). Prerekvizita CORE = building-block `ModularPlatform.Ai` (CONVENTIONS §17). Competitor learnings: Langfuse/LangSmith/Braintrust/Ragas · HumanLayer/Argilla · LiteLLM/Portkey/Helicone · OpenAI/Vectara/Glean.
