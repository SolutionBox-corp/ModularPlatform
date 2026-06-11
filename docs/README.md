# ModularPlatform — docs index

> Mapa dokumentace. **`CLAUDE.md` (root) je zákon** (jak se staví). Tady je rozcestník: co je co, k jakému datu, a
> **co je autoritativní pro aktuální stav.** Stav k **2026-06-11**: backend **181/181 testů, build 0/0**, pushnuto na
> `main`. Multi-tenancy + frontend jsou **navržené, ne postavené**.

## Aktuální / autoritativní
| Dokument | Co to je | Stav |
|---|---|---|
| **`../CLAUDE.md`** | Zákon — všechny patterny, zákony, konvence, §4 „už vyřešeno", §10 „ještě ne / ASK" | ✅ aktuální |
| **`feature-coverage.md`** | **Autoritativní per-feature USE/Edge/Test coverage** (78 features, verdikty, gapy, deferred-with-rationale) | ✅ aktuální (181 testů) |
| **`stability-audit-2026-06-10.md`** | Stabilizační audit + fix-wave log (108→181, /fullreview + coverage + edge-case waves; rate-limiter ordering bug atd.) | ✅ aktuální |
| **`multitenancy-and-infra.md`** | **NAVRŽENO, ne postaveno** — B2B subdoména-per-tenant, platform-admin, per-tenant module entitlements, Caddy wildcard DNS-01, pool→silo, Pulumi separace | 🟡 design |

## Frontend (mimo repo)
| Dokument | Co | Stav |
|---|---|---|
| `~/Desktop/ModularPlatform-Frontend-Handoff.md` | Handoff pro FE agenta (Next.js 15, stack, page map, design page, tenancy §0.5) | 🟡 design |
| skills `modularplatform-frontend` + `frontend-feature-slice` (`.claude/skills/`) | Encoded FE patterny (architektura + feature slice) | 🟡 design |

## Historické / referenční (popisují PROČ/JAK se feature postavila — neaktualizují se)
| Dokument | Co |
|---|---|
| `ROADMAP.md` | Původní „co všechno musí být" mapa (backend scope; postaveno k 06-10). **Pro aktuální coverage viz `feature-coverage.md`.** |
| `test-scenarios.md` | Původní Given/When/Then test plán. **Autoritativní aktuální coverage = `feature-coverage.md`** (část tehdejších ▢ gapů je už ✓). |
| `HANDOFF.md` | „Next-agent" mapa backendu (build/run/test + orientace). |
| `audit-pii-encryption-design.md`, `pii-column-encryption-design.md` | Design rationale PII/audit šifrování + column encryption + blind index. |
| `billing-revenue-design.md` | Design rationale Billing commerce (packages/subscriptions/coupons/saga). |
| `ops-jobs-design.md` | Design rationale ops jobů (reconcile/messaging-health/retention). |
| `realtime-replay-design.md` | Design rationale realtime replay bufferu. |

## Skills (`.claude/skills/`, committed)
`building-modularplatform-feature` · `adding-a-module` · `adding-billing-command` · `writing-modularplatform-tests`
(backend) · `modularplatform-frontend` · `frontend-feature-slice` (frontend).

---
*Když něco v historickém docu odporuje `CLAUDE.md` / `feature-coverage.md`, platí ty aktuální.*
