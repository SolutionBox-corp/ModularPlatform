---
name: adding-billing-command
description: Add a credit-ledger or Stripe command in the Billing module. Use when touching credits, top-ups, spend/reservations, subscriptions, coupons, packages, or Stripe webhooks. Enforces the append-only ledger + EF-native atomic guard + idempotent-webhook rules so money is never corrupted.
---

# Adding a Billing command

**Read `CLAUDE.md` §4 (Credits/money row) + "Money correctness (Billing)" first.** Money correctness is
non-negotiable. **Copy the existing handlers — `ReserveCreditsHandler` / `ConfirmSpendHandler` / `CreditTopUpHandler`
are the canonical, tested shapes. Do not invent a new flow.** Proven by `BillingConcurrencyTests` + `BillingLedgerTests`.

## The ledger model (never violate)
- `credit_account` — one per user; **stored** `posted`/`pending`/`available` are AUTHORITATIVE, maintained
  transactionally. Invariant **`available = posted − pending`** preserved by arithmetic in every handler.
- `credit_entry` — **append-only, immutable**. Never UPDATE/DELETE. Has a **UNIQUE `idempotency_key`** (the dedup guard).
- `credit_bucket` — per top-up with `expires_at`; spend draws soonest-to-expire (FIFO). DB CHECK constraints keep
  posted/available/pending ≥ 0 as a backstop.

## EF-native rules — NEVER raw SQL
1. **DEBIT path = atomic conditional `ExecuteUpdate` guard** (the EF pessimistic equivalent — the UPDATE locks the
   row + evaluates the guard atomically, so DB serializes; no double-spend, no retry storm):
   ```csharp
   var debited = await db.CreditAccounts
       .Where(a => a.Id == accountId && a.Available >= amount)
       .ExecuteUpdateAsync(s => s
           .SetProperty(a => a.Available, a => a.Available - amount)
           .SetProperty(a => a.Pending,   a => a.Pending   + amount), ct);
   if (debited == 0) throw new BusinessRuleException("credit.insufficient_balance", "…");
   ```
   Wrap the guard + the hold/entry insert in `BeginTransactionAsync … SaveChangesAsync … CommitAsync` so the reserve
   always has a matching hold.
2. **Confirm/release/expire/top-up = tracked entities + xmin** (the `ConcurrencyRetryBehavior` handles conflicts —
   it clears the tracker before retry). Maintain the invariant by arithmetic. **Idempotency** = the UNIQUE
   `idempotency_key` + `catch (DbUpdateException ex) when (ex is not DbUpdateConcurrencyException)` → re-read and
   return the already-applied state.
3. **Outbox handlers** (confirm/top-up publish events): `await outbox.SaveChangesAndFlushMessagesAsync()` **IS the
   commit** — never also call `tx.CommitAsync`.
4. `GetCreditBalance` returns the STORED `available` (so the shown balance == what a reservation will allow).

## Stripe webhook (copy `StripeWebhookEndpoint`)
- Thin ingest: verify signature against **raw body bytes** (`EventUtility.ConstructEvent`), persist `StripeEvent`
  (UNIQUE `StripeEventId`) AND enqueue `ProcessStripeEventMessage` **atomically via `IDbContextOutbox`**, return 200.
- `.AllowAnonymous().DisableRateLimiting()` (Stripe bursts from many IPs).
- A **Worker** handler runs the idempotent top-up (key = Stripe event id). Events arrive out of order — reconcile
  against object state. A daily reconciliation Job re-applies events (Stripe = source of truth).

## Subscriptions / coupons / packages
Subscriptions, coupons, promo codes, proration → **Stripe** (don't re-implement discount math). Credit packages →
our DB (`credit_package`), each mapped to a one-time Stripe Price. Bound amounts in the validator (`> 0` and `≤ max`).

## GDPR
The append-only ledger is **retained for AML/tax** — `BillingPersonalDataEraser` is a documented near-no-op; never
delete ledger rows for erasure.

## NEVER
Raw SQL · mutate a balance by read-then-write outside an atomic guard/transaction · update/delete a ledger entry ·
trust a stale balance · double-credit (use the UNIQUE key) · ledger work inline in the webhook request · assume
webhook ordering.

## Verify
`dotnet test src/modules/Billing/ModularPlatform.Billing.Tests` (concurrency + idempotency cases) green; `dotnet build` 0/0.
