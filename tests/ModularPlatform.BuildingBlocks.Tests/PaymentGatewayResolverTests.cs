using ModularPlatform.Abstractions;
using ModularPlatform.Payments;
using Shouldly;

namespace ModularPlatform.BuildingBlocks.Tests;

public sealed class PaymentGatewayResolverTests
{
    private sealed class FixedClock : IClock
    {
        public DateTimeOffset UtcNow => DateTimeOffset.UnixEpoch;
    }

    private sealed class StubStore(ResolvedPaymentConfig? config) : IPaymentConfigStore
    {
        public PaymentPlane Plane => PaymentPlane.Tenant;

        public Task<ResolvedPaymentConfig?> GetAsync(Guid tenantId, PaymentPlane plane, CancellationToken ct = default) =>
            Task.FromResult(config);
    }

    private static IPaymentGatewayResolver Resolver(ResolvedPaymentConfig? config) =>
        new PaymentGatewayResolver([new StubStore(config)], new HttpClient(), new FixedClock());

    [Fact]
    public async Task Resolves_the_fake_provider()
    {
        var gateway = await Resolver(new ResolvedPaymentConfig(PaymentProvider.Fake, "CZK", Active: true))
            .ResolveAsync(Guid.NewGuid(), PaymentPlane.Tenant);

        gateway.ShouldBeOfType<FakePaymentGateway>();
    }

    [Fact]
    public async Task Resolves_stripe_and_gopay_to_their_adapters()
    {
        var stripe = await Resolver(new ResolvedPaymentConfig(
                PaymentProvider.Stripe, "EUR", Active: true, Stripe: new StripeConfig("sk_test", "whsec")))
            .ResolveAsync(Guid.NewGuid(), PaymentPlane.Tenant);
        stripe.ShouldBeOfType<StripePaymentGateway>();
        stripe.Capabilities.SignedWebhooks.ShouldBeTrue();

        var gopay = await Resolver(new ResolvedPaymentConfig(
                PaymentProvider.GoPay, "CZK", Active: true,
                GoPay: new GoPayCredentials(123, "cid", "secret", "https://gw.sandbox.gopay.com/api", "https://x/webhook")))
            .ResolveAsync(Guid.NewGuid(), PaymentPlane.Tenant);
        gopay.ShouldBeOfType<GoPayPaymentGateway>();
        gopay.Capabilities.SignedWebhooks.ShouldBeFalse();
    }

    [Fact]
    public async Task A_missing_config_fails_with_not_configured()
    {
        var ex = await Should.ThrowAsync<PaymentGatewayUnavailableException>(
            () => Resolver(null).ResolveAsync(Guid.NewGuid(), PaymentPlane.Tenant));
        ex.ErrorCode.ShouldBe("payment.gateway_not_configured");
    }

    [Fact]
    public async Task An_inactive_config_fails_with_inactive()
    {
        var ex = await Should.ThrowAsync<PaymentGatewayUnavailableException>(
            () => Resolver(new ResolvedPaymentConfig(PaymentProvider.Fake, "CZK", Active: false))
                .ResolveAsync(Guid.NewGuid(), PaymentPlane.Tenant));
        ex.ErrorCode.ShouldBe("payment.gateway_inactive");
    }
}
