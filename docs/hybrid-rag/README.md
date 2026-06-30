# HybridRag — UC/EC katalog

Vyčerpávající číslovaný katalog **use cases** a **edge cases** pro nový modul `HybridRag` (hybrid vektor + knowledge-graph RAG) v ModularPlatform. **Design dokumentace — produkční kód zatím neexistuje**; katalog je zdroj pravdy chování, podklad pro test scaffolding a implementaci. Plán: `~/.claude/plans/pojdme-udelat-plan-na-moonlit-kahan.md`.

**Číslování:** `UC-NN-MM` (oblast-pořadí), `EC-NN-MM-KK` (oblast-UC-EC). Severity/Priorita P0 (kritické) → P3 (nice-to-have).

## Klíčová rozhodnutí (frozen)
- **Graf** = relační edge tabulky (`GraphNode`/`GraphEdge`) + pgvector přes EF/LINQ (NE Apache AGE/Neo4j; graf dostane RLS/audit/xmin zdarma).
- **Korpus dvouvrstvý**: `Scope = Tenant | User`. User hledá v tenant korpusu + svých privátních současně; tenant-admin (permission-gated) napříč všemi usery v tenantu.
- **Lexikální retrieval = pravé BM25 přes ParadeDB `pg_search`** (`@@@`, IDF + length-norm) by default; `ts_rank_cd` LINQ jako fallback. BM25 = úzká dokumentovaná raw-SQL carve-out (parametrizovaný `FromSqlInterpolated`).
- **Vektor** = pgvector HNSW, `CosineDistance` LINQ (žádný raw SQL). **RRF** k=60 v C#. **Rerank** = Cohere `rerank-3.5`. **Embed** = OpenAI `text-embedding-3-large`. **Chat** = Claude. Fake gateways pod `Rag:UseFakeGateways`.

## ⚠️ Otevřená rozhodnutí PŘED implementací (§11 — STOP, neimprovizovat)
- **PII × lexikální index (kritické):** `Chunk.Content` je `[Encrypted][PersonalData]` at-rest, ale BM25/`ts_rank` index potřebuje **plaintext** tokeny → plaintext lexikální index nad PII oslabuje crypto-shred / GDPR erasure záruku. Index nad ciphertextem nejde. Rozhodnout: lexikální retrieval jen pro ne-PII collections vs oddělený režim vs akceptovaný plaintext index. (`EC-06-09-05`, oblast 20)
- Další parkovaná rozhodnutí na koncích souborů 06/11/13/16 (pg_search DSL, auto-fallback provideru, entity-merge threshold apod.).

## Pokrytí (roll-up) — **24 oblastí, 268 UC, 1694 EC**

| # | Oblast | #UC | #EC | P0 | P1 | P2 | P3 |
|---|---|---:|---:|---:|---:|---:|---:|
| [00](00-collections-tenancy.md) | Collections & tenancy | 13 | 103 | 36 | 30 | 28 | 9 |
| [01](01-document-ingestion.md) | Document ingestion (upload & text extraction) | 8 | 57 | 19 | 30 | 8 | 1 |
| [02](02-chunking-contextualization.md) | Chunking & contextualization | 11 | 71 | 31 | 21 | 13 | 6 |
| [03](03-embedding.md) | Embedding generation | 9 | 53 | 12 | 24 | 14 | 3 |
| [04](04-durable-ingest-saga.md) | Durable ingest saga & indexing | 15 | 110 | 33 | 44 | 29 | 4 |
| [05](05-dense-vector-retrieval.md) | Dense vector retrieval (pgvector) | 11 | 58 | 26 | 20 | 12 | 0 |
| [06](06-sparse-lexical-retrieval.md) | Sparse / lexical retrieval (BM25) | 12 | 82 | 18 | 30 | 24 | 10 |
| [07](07-hybrid-fusion-rrf.md) | Hybrid fusion (RRF) | 12 | 56 | 12 | 18 | 15 | 11 |
| [08](08-reranking.md) | Reranking (cross-encoder) | 12 | 54 | 17 | 19 | 17 | 1 |
| [09](09-freshness-versioning.md) | Freshness & versioning | 11 | 70 | 18 | 23 | 25 | 4 |
| [10](10-graph-extraction.md) | Knowledge graph extraction | 12 | 80 | 20 | 31 | 28 | 0 |
| [11](11-entity-resolution.md) | Entity resolution | 12 | 76 | 32 | 26 | 17 | 1 |
| [12](12-graph-retrieval.md) | Graph retrieval (local / global / community) | 11 | 78 | 19 | 37 | 20 | 2 |
| [13](13-answer-synthesis-citations.md) | Answer synthesis & citations | 15 | 83 | 15 | 31 | 27 | 10 |
| [14](14-prompt-caching-context.md) | Prompt caching & context assembly | 8 | 37 | 12 | 13 | 11 | 1 |
| [15](15-mcp-server-tooluse.md) | MCP server & tool-use | 11 | 58 | 21 | 19 | 15 | 3 |
| [16](16-multitenant-isolation-security.md) | Multi-tenant isolation & security | 10 | 68 | 29 | 28 | 11 | 0 |
| [17](17-graceful-degradation.md) | Graceful degradation & resilience | 14 | 77 | 13 | 36 | 24 | 4 |
| [18](18-evaluation.md) | Evaluation (offline gate + online sampling) | 9 | 61 | 25 | 21 | 13 | 2 |
| [19](19-observability-cost.md) | Observability & cost | 12 | 82 | 27 | 27 | 28 | 0 |
| [20](20-gdpr-pii-lifecycle.md) | GDPR / PII / data lifecycle | 11 | 73 | 28 | 28 | 14 | 3 |
| [21](21-streaming-realtime.md) | Streaming & realtime | 9 | 54 | 13 | 18 | 19 | 4 |
| [22](22-rate-limiting-abuse.md) | Rate limiting & abuse | 8 | 53 | 11 | 23 | 17 | 2 |
| [23](23-admin-management.md) | Admin / management (catalogue, reindex, delete) | 12 | 100 | 39 | 31 | 24 | 6 |
| | **CELKEM** | **268** | **1694** | **526** | **628** | **453** | **87** |

> Pozn.: P-rozpad je součet Severity (EC) + Priorita (UC) výskytů na oblast.
