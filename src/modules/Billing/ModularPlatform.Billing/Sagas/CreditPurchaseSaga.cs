using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ModularPlatform.Abstractions;
using ModularPlatform.Billing.Contracts;
using ModularPlatform.Billing.Features.Credits.CreditTopUp;
using ModularPlatform.Cqrs;
using ModularPlatform.Persistence.Entities;
using Wolverine;

namespace ModularPlatform.Billing.Sagas;

/// <summary>
/// THE CANONICAL PLATFORM SAGA (Wolverine, Postgres-backed via EF in <c>credit_purchase_sagas</c>) — the
/// self-healing multi-step workflow from the architecture plan §4:
/// <c>checkout created → Stripe confirmed → credits granted → completion event → (or) abandon timeout</c>.
/// Rules a new saga must copy:
/// <list type="bullet">
/// <item>PUBLIC class (Wolverine codegen), registered via <c>Discovery.IncludeType</c>, runs in the Worker.</item>
/// <item>The saga NEVER mutates money itself — it dispatches the existing idempotent command
/// (<see cref="CreditTopUpCommand"/>, key <c>purchase:{id}</c>), so replays/races converge in the ledger.</item>
/// <item>Compensation is a STATE, not an exception: the timeout marks the purchase Abandoned (nothing was
/// granted, nothing to undo) and a LATE confirmation still grants via the static <see cref="NotFound"/> hook.</item>
/// <item>Cascaded messages (the integration event) ride the same transaction as the saga state change.</item>
/// <item>DELIBERATE deviation from the textbook lifecycle: <c>MarkCompleted()</c> is NOT called — Wolverine
/// deletes completed sagas, and this row doubles as the user-facing purchase record
/// (<c>GET /billing/purchases/{id}</c>). Terminal states are guarded by <see cref="Status"/> instead.</item>
/// </list>
/// </summary>
public sealed class CreditPurchaseSaga : Saga, IUserOwned
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public Guid PackageId { get; set; }
    public string CheckoutSessionId { get; set; } = string.Empty;
    public long CreditAmount { get; set; }
    public int? BucketExpiryDays { get; set; }
    public string Status { get; set; } = "Pending";
    public DateTimeOffset StartedAt { get; set; }
    public DateTimeOffset? ResolvedAt { get; set; }

    public static (CreditPurchaseSaga, CreditPurchaseTimeout) Start(CreditPurchaseStarted started, IClock clock)
    {
        var saga = new CreditPurchaseSaga
        {
            Id = started.Id,
            UserId = started.UserId,
            PackageId = started.PackageId,
            CheckoutSessionId = started.CheckoutSessionId,
            CreditAmount = started.CreditAmount,
            BucketExpiryDays = started.BucketExpiryDays,
            Status = "Pending",
            StartedAt = clock.UtcNow,
        };
        return (saga, new CreditPurchaseTimeout(started.Id, started.TimeoutMinutes));
    }

    public async Task<object[]> Handle(
        CreditPurchaseConfirmed confirmed, IDispatcher dispatcher, IClock clock, CancellationToken ct)
    {
        // Terminal-state guard FIRST: a duplicate confirmation on an already-Completed saga must short-circuit
        // BEFORE re-dispatching anything (no wasted command, no risk of a second completion event escaping).
        if (Status == "Completed")
        {
            return []; // Duplicate confirmation — already granted and announced.
        }

        // Grant through the ledger's idempotent top-up — a duplicate Stripe event, a saga replay, or a
        // confirmation arriving after the abandon timeout cannot double-credit (UNIQUE idempotency_key). A late
        // payment after the abandon timeout reaches here with Status="Abandoned" and correctly flips to Completed.
        await dispatcher.Send(new CreditTopUpCommand(
            UserId, CreditAmount, BucketExpiryDays, IdempotencyKey: $"purchase:{Id}"), ct);

        Status = "Completed";
        ResolvedAt = clock.UtcNow;

        return
        [
            new CreditPurchaseCompletedIntegrationEvent(
                EventId: Guid.CreateVersion7(),
                OccurredAt: clock.UtcNow,
                UserId: UserId,
                PurchaseId: Id,
                PackageId: PackageId,
                CreditAmount: CreditAmount),
        ];
    }

    public void Handle(CreditPurchaseTimeout timeout, IClock clock)
    {
        if (Status != "Pending")
        {
            return; // Already resolved — the timeout is a no-op.
        }

        // Compensation: the checkout window elapsed without a confirmation. Nothing was charged or granted,
        // so abandoning is just a state transition; Stripe expires the open session on its side. A LATE
        // payment after this still grants (the Confirmed handler is idempotent and flips the state back).
        Status = "Abandoned";
        ResolvedAt = clock.UtcNow;
    }

    /// <summary>
    /// A confirmation arriving AFTER the saga timed out (row already deleted): the customer paid late —
    /// still grant, idempotently. Money is never lost to a workflow timeout.
    /// </summary>
    public static Task NotFound(CreditPurchaseConfirmed confirmed, IDispatcher dispatcher, CancellationToken ct) =>
        dispatcher.Send(new CreditTopUpCommand(
            confirmed.UserId, confirmed.CreditAmount, confirmed.BucketExpiryDays,
            IdempotencyKey: $"purchase:{confirmed.Id}"), ct);

    // No NotFound for CreditPurchaseTimeout: a TimeoutMessage is ignored by Wolverine when the saga is gone.
}

internal sealed class CreditPurchaseSagaConfiguration : IEntityTypeConfiguration<CreditPurchaseSaga>
{
    public void Configure(EntityTypeBuilder<CreditPurchaseSaga> builder)
    {
        builder.ToTable("credit_purchase_sagas");
        builder.HasKey(s => s.Id);
        builder.Property(s => s.Id).ValueGeneratedNever();
        builder.Property(s => s.CheckoutSessionId).HasMaxLength(256).IsRequired();
        builder.Property(s => s.Status).HasMaxLength(16).IsRequired();
        builder.HasIndex(s => s.UserId);
        builder.HasIndex(s => s.CheckoutSessionId);
        // Wolverine's Saga base Version backs optimistic concurrency in saga storage.
        builder.Property(s => s.Version);
    }
}
