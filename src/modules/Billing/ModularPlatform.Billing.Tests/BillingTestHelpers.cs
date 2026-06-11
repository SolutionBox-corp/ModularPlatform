using Microsoft.Extensions.DependencyInjection;
using ModularPlatform.Billing.Features.Credits.CreditTopUp;
using ModularPlatform.Cqrs;
using ModularPlatform.IntegrationTesting;

namespace ModularPlatform.Billing.Tests;

/// <summary>
/// Seeds credits the way a real payment does — by dispatching the INTERNAL <see cref="CreditTopUpCommand"/>
/// in-process (the same idempotent grant primitive the <c>CreditPurchaseSaga</c>, the subscription grant and
/// the Stripe webhook router use after a real payment). The public <c>POST /billing/credits/topup</c> endpoint
/// is admin-only (<c>billing.manage</c>) — an ordinary user can no longer self-mint over HTTP — so ledger
/// tests arrange their starting balance through the command, which is the honest post-payment seam.
/// </summary>
internal static class BillingTestHelpers
{
    public static async Task<CreditTopUpResponse> GrantCreditsAsync(
        this PlatformApiFactory fixture, Guid userId, long amount,
        int? bucketExpiryDays = null, string? idempotencyKey = null)
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var dispatcher = scope.ServiceProvider.GetRequiredService<IDispatcher>();
        return await dispatcher.Send(new CreditTopUpCommand(
            userId, amount, bucketExpiryDays, idempotencyKey ?? $"seed-{Guid.CreateVersion7():N}"));
    }

    /// <summary>Dispatches any command in-process (a fresh DI scope) — for ledger commands a test drives directly.</summary>
    public static async Task<TResult> DispatchAsync<TResult>(this PlatformApiFactory fixture, ICommand<TResult> command)
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var dispatcher = scope.ServiceProvider.GetRequiredService<IDispatcher>();
        return await dispatcher.Send(command);
    }
}
