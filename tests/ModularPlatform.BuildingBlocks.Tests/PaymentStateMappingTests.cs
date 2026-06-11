using ModularPlatform.Payments;
using Shouldly;

namespace ModularPlatform.BuildingBlocks.Tests;

public sealed class PaymentStateMappingTests
{
    [Theory]
    [InlineData("succeeded", PaymentState.Paid)]
    [InlineData("complete", PaymentState.Paid)]
    [InlineData("requires_capture", PaymentState.Authorized)]
    [InlineData("canceled", PaymentState.Canceled)]
    [InlineData("processing", PaymentState.Pending)]
    [InlineData("unpaid", PaymentState.Pending)]
    [InlineData(null, PaymentState.Pending)]
    [InlineData("something_new", PaymentState.Pending)]
    public void Stripe_statuses_normalize(string? raw, PaymentState expected) =>
        PaymentStateMapping.FromStripe(raw).ShouldBe(expected);

    [Theory]
    [InlineData("PAID", PaymentState.Paid)]
    [InlineData("AUTHORIZED", PaymentState.Authorized)]
    [InlineData("CREATED", PaymentState.Created)]
    [InlineData("PAYMENT_METHOD_CHOSEN", PaymentState.Pending)]
    [InlineData("CANCELED", PaymentState.Canceled)]
    [InlineData("TIMEOUTED", PaymentState.Expired)]
    [InlineData("REFUNDED", PaymentState.Refunded)]
    [InlineData("PARTIALLY_REFUNDED", PaymentState.PartiallyRefunded)]
    [InlineData(null, PaymentState.Pending)]
    public void GoPay_states_normalize(string? raw, PaymentState expected) =>
        PaymentStateMapping.FromGoPay(raw).ShouldBe(expected);

    [Fact]
    public void Unknown_provider_status_never_maps_to_paid()
    {
        PaymentStateMapping.FromStripe("weird").ShouldNotBe(PaymentState.Paid);
        PaymentStateMapping.FromGoPay("WEIRD").ShouldNotBe(PaymentState.Paid);
    }
}
