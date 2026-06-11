using ModularPlatform.Payments;
using Shouldly;

namespace ModularPlatform.BuildingBlocks.Tests;

public sealed class FakePaymentGatewayTests
{
    private static CheckoutRequest Checkout(long amount = 10_000) => new(
        ReferenceId: Guid.CreateVersion7().ToString("N"),
        AmountMinorUnits: amount,
        Currency: "CZK",
        Mode: CheckoutMode.Payment,
        Description: "Test",
        Metadata: new Dictionary<string, string> { ["tenant_id"] = "t1" },
        SuccessUrl: "https://x/ok",
        CancelUrl: "https://x/cancel");

    [Fact]
    public async Task Checkout_creates_a_payment_in_Created_with_the_redirect_and_id()
    {
        var gateway = new FakePaymentGateway();

        var result = await gateway.CreateCheckoutAsync(Checkout());

        result.ProviderPaymentId.ShouldNotBeNullOrWhiteSpace();
        result.RedirectUrl.ShouldContain(result.ProviderPaymentId);
        (await gateway.GetPaymentStateAsync(result.ProviderPaymentId)).State.ShouldBe(PaymentState.Created);
        gateway.CreatedCheckouts.ShouldHaveSingleItem();
    }

    [Fact]
    public async Task Verify_notification_re_fetches_the_authoritative_state_not_the_payload()
    {
        var gateway = new FakePaymentGateway();
        var result = await gateway.CreateCheckoutAsync(Checkout());
        gateway.SetState(result.ProviderPaymentId, PaymentState.Paid);

        // The notification payload is a lie ("Created"); the re-fetch wins.
        var snapshot = await gateway.VerifyNotificationAsync(new NotificationContext(
            RawBody: "Created", SignatureHeader: null,
            Query: new Dictionary<string, string> { ["id"] = result.ProviderPaymentId }));

        snapshot.State.ShouldBe(PaymentState.Paid);
        snapshot.Metadata["tenant_id"].ShouldBe("t1");
    }

    [Fact]
    public async Task Full_and_partial_refunds_map_to_the_neutral_states()
    {
        var gateway = new FakePaymentGateway();
        var a = await gateway.CreateCheckoutAsync(Checkout(10_000));
        var b = await gateway.CreateCheckoutAsync(Checkout(10_000));

        (await gateway.RefundAsync(a.ProviderPaymentId, null)).State.ShouldBe(PaymentState.Refunded);
        (await gateway.RefundAsync(b.ProviderPaymentId, 4_000)).State.ShouldBe(PaymentState.PartiallyRefunded);
    }

    [Fact]
    public void Capabilities_are_overridable_to_model_a_specific_provider()
    {
        // e.g. a GoPay-shaped fake: no signed webhook, no native subscription/coupon/tax.
        var goPayish = new FakePaymentGateway(new GatewayCapabilities(
            SignedWebhooks: false, NativeSubscriptions: false, NativeCoupons: false, NativeTax: false, PreAuthorization: true));

        goPayish.Capabilities.SignedWebhooks.ShouldBeFalse();
        goPayish.Capabilities.NativeSubscriptions.ShouldBeFalse();
        new FakePaymentGateway().Capabilities.SignedWebhooks.ShouldBeTrue();
    }
}
