# HybridRag — UC/EC katalog

Vyčerpávající číslovaný katalog **use cases** a **edge cases** pro nový modul `HybridRag` (hybrid vektor + knowledge-graph RAG) v ModularPlatform. **Design dokumentace — produkční kód zatím neexistuje**; katalog je zdroj pravdy chování, podklad pro test scaffolding a implementaci. Plán: `~/.claude/plans/pojdme-udelat-plan-na-moonlit-kahan.md`.

> 📐 **[CONVENTIONS.md](CONVENTIONS.md) je nadřazený zdroj pravdy pro pojmenování.** Katalog generovalo 24 paralelních agentů, takže jednotlivé soubory místy driftují (route prefix, názvy entit/tabulek, permissions, enum hodnoty). Kanonickou volbu + konsolidovaná otevřená rozhodnutí drží CONVENTIONS.md — kde se soubor rozchází, vyhrává CONVENTIONS.

**Číslování:** `UC-NN-MM` (oblast-pořadí), `EC-NN-MM-KK` (oblast-UC-EC). Severity/Priorita P0 (kritické) → P3 (nice-to-have). Sekce „Doplňky z completeness review" na konci souborů = nálezy z adversariálního review passu.

## Klíčová rozhodnutí (frozen — detail v CONVENTIONS.md)
- **Graf** = relační edge tabulky (`GraphNode`/`GraphEdge`) + pgvector přes EF/LINQ (NE Apache AGE/Neo4j; RLS/audit/xmin zdarma).
- **Korpus dvouvrstvý**: `Scope = Tenant | User`. User hledá v tenant korpusu + svých privátních současně; tenant-admin (permission-gated) napříč všemi usery.
- **Lexikál = pravé BM25 přes ParadeDB `pg_search`** (`@@@`, IDF + length-norm) by default; `ts_rank_cd` LINQ fallback. Raw-SQL carve-out (parametrizovaný `FromSqlInterpolated`).
- **Vektor** = pgvector HNSW, `CosineDistance` LINQ, **`halfvec(3072)`** (HNSW >2000 dim). **RRF** k=60 v C#. **Rerank** = Cohere `rerank-3.5`. **Embed** = OpenAI `text-embedding-3-large`. **Chat** = Claude. Fake pod `Rag:UseFakeGateways`.

## ⚠️ Otevřená rozhodnutí PŘED implementací (§11 — STOP) — plný seznam v [CONVENTIONS.md §12](CONVENTIONS.md)
1. **PII × plaintext lexikální/graf index (KRITICKÉ):** `[Encrypted]` Content/graf klíče vs BM25/tsvector/lookup potřebují plaintext → přežije crypto-shred → GDPR erasure díra.
2. **Tenant-level encryption key (KEK/KMS):** crypto-shred je per-USER; tenant DEK infra není (NOT YET).
3. **Company-read (tenant-wide) RLS path:** 00 prezentuje jako hotové, 05 jako nerozhodnuté.
4. **Druhý rerank provider** (bge self-hosted port) — rozšíření vs §11.
5. **Základní search permission** — existuje (15) vs neexistuje (13/16)?

## Pokrytí (roll-up) — **24 oblastí, 269 UC, 1736 EC** (vč. completeness-review doplňků)

| # | Oblast | #UC | #EC | P0 | P1 | P2 | P3 |
|---|---|---:|---:|---:|---:|---:|---:|
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
| | **CELKEM** | **269** | **1736** | **536** | **656** | **453** | **87** |

> P-rozpad = součet Severity (EC) + Priorita (UC) výskytů. Review pass: 4 adversariální kritici → ~38 doplňkových P0/P1 EC + 34 konzistenčních nálezů (vyřešeno v CONVENTIONS.md).
