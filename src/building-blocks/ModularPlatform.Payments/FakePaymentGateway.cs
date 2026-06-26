using System.Collections.Concurrent;

namespace ModularPlatform.Payments;

/// <summary>
/// In-memory <see cref="IPaymentGateway"/> for tests and the integration harness — NEVER a real deployment. Models
/// the re-fetch contract: a checkout creates a stored payment a test can transition (<see cref="SetState"/>) before
/// driving a webhook through <see cref="VerifyNotificationAsync"/>. Captures created checkouts for assertions.
/// </summary>
public sealed class FakePaymentGateway(GatewayCapabilities? capabilities = null) : IPaymentGateway
{
    private readonly ConcurrentDictionary<string, PaymentSnapshot> _payments = new();
    private readonly ConcurrentBag<CheckoutRequest> _created = [];
    private Exception? _nextCheckoutFailure;

    public GatewayCapabilities Capabilities { get; } =
        capabilities ?? new GatewayCapabilities(true, true, true, true, true);

    /// <summary>Checkouts created through this fake (newest-unordered) — for test assertions on amount/metadata.</summary>
    public IReadOnlyCollection<CheckoutRequest> CreatedCheckouts => _created;

    public Task<CheckoutResult> CreateCheckoutAsync(CheckoutRequest request, CancellationToken ct = default)
    {
        if (Interlocked.Exchange(ref _nextCheckoutFailure, null) is { } failure)
        {
            return Task.FromException<CheckoutResult>(failure);
        }

        _created.Add(request);
        var id = $"fake_{Guid.CreateVersion7():N}";
        _payments[id] = new PaymentSnapshot(
            id, PaymentState.Created, request.AmountMinorUnits, request.Currency, request.Metadata);
        return Task.FromResult(new CheckoutResult(id, $"https://fake.gateway/checkout/{id}"));
    }

    public Task<PaymentSnapshot> GetPaymentStateAsync(string providerPaymentId, CancellationToken ct = default) =>
        _payments.TryGetValue(providerPaymentId, out var snapshot)
            ? Task.FromResult(snapshot)
            : throw new KeyNotFoundException($"Unknown fake payment '{providerPaymentId}'.");

    public Task<RefundResult> RefundAsync(string providerPaymentId, long? amountMinorUnits, CancellationToken ct = default)
    {
        var current = _payments[providerPaymentId];
        var full = amountMinorUnits is null || amountMinorUnits >= current.AmountMinorUnits;
        var state = full ? PaymentState.Refunded : PaymentState.PartiallyRefunded;
        _payments[providerPaymentId] = current with { State = state };
        return Task.FromResult(new RefundResult($"refund_{Guid.CreateVersion7():N}", state));
    }

    public Task<PaymentSnapshot> VerifyNotificationAsync(NotificationContext context, CancellationToken ct = default)
    {
        // Re-fetch model: the notification only points at an id; the stored state is authoritative.
        var id = context.Query.TryGetValue("id", out var value) ? value : context.RawBody;
        return GetPaymentStateAsync(id, ct);
    }

    public Task<bool> ValidateCredentialsAsync(CancellationToken ct = default) => Task.FromResult(true);

    /// <summary>Transitions a seeded payment to a new state (e.g. mark it Paid before a webhook).</summary>
    public void SetState(string providerPaymentId, PaymentState state) =>
        _payments[providerPaymentId] = _payments[providerPaymentId] with { State = state };

    public void FailNextCheckout(Exception? exception = null) =>
        Interlocked.Exchange(ref _nextCheckoutFailure, exception ?? new InvalidOperationException("Fake checkout failure."));
}
