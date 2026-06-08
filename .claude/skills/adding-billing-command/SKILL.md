---
name: adding-billing-command
description: Add a credit-ledger or Stripe command in the Billing module. Use when touching credits, top-ups, spend/reservations, subscriptions, coupons, packages, or Stripe webhooks. Enforces the append-only ledger + pessimistic-lock + idempotent-webhook rules so money is never corrupted.
---

# Adding a Billing command

**Read `CLAUDE.md` §4 (billing row) first.** Money correctness is non-negotiable. The ledger is the source of
truth for the wallet; Stripe holds the money.

## The ledger model (never violate)
- `credit_account` — one per user/tenant; cached `posted`/`pending`/`available` (a projection, verified against
  entries inside the lock — never trusted alone).
- `credit_entry` — **append-only, immutable**. Never UPDATE/DELETE an entry. Each `transaction_id` groups a
  balanced set. Has `idempotency_key`.
- `credit_bucket` — per top-up with `expires_at`; spend draws soonest-to-expire (FIFO).

## Rules
1. **Debit/reserve = pessimistic.** `SELECT … FOR NO KEY UPDATE` on the account row, then check-and-debit in ONE
   serialized transaction. **Never** optimistic concurrency on the debit path. Invariant everywhere: `available >= 0`.
2. **`available = posted - active_holds`**, computed so an EXPIRED reservation is ignored by the query even before
   the sweep job runs (the Jobs sweep is cleanup, not correctness).
3. **Reservations:** `ReserveCredits` (hold) → `ConfirmSpend` (posted debit) → `ReleaseHold`. Every reservation has
   a hard `expires_at`.
4. **Top-up is idempotent** by `idempotency_key` (Stripe event id) — applying the same event twice = ONE credit.

## Stripe webhook (copy this shape)
- The endpoint is a thin ingest: verify signature against the **raw body bytes** (`EventUtility.ConstructEvent`),
  persist `event.id` under a UNIQUE constraint **in the same transaction**, return **200 immediately**, enqueue.
- A **Worker** handler does the idempotent ledger top-up. Events arrive **out of order** — reconcile against object
  state, never assume sequence.
- A daily **reconciliation Job** (Jobs host) re-applies Stripe events for a window (Stripe = source of truth).

## Subscriptions / coupons / packages
- Subscriptions, coupons, promotion codes, proration → **Stripe** (don't re-implement discount math).
- Credit packages → our DB (`credit_package`), each mapped to a one-time Stripe Price.

## Commands you extend
`CreditTopUp, ReserveCredits, ConfirmSpend, ReleaseHold, ExpireCredits, HandleStripeEvent` — follow the
building-modularplatform-feature skill for the slice mechanics.

## Never
Mutate a balance in place. Update/delete a ledger entry. Trust the cached balance outside the lock. Do ledger work
inline in the webhook HTTP request. Assume webhook ordering.
