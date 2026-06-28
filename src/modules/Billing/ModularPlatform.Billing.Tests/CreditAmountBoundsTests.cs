using FluentValidation.Results;
using ModularPlatform.Billing.Contracts;
using ModularPlatform.Billing.Features.Credits.CreditTopUp;
using ModularPlatform.Billing.Features.Credits.ReserveCredits;
using Shouldly;

namespace ModularPlatform.Billing.Tests;

/// <summary>
/// A7 money hardening: the credit-amount validators reject non-positive and oversized amounts BEFORE any
/// ledger arithmetic runs, so <c>Posted/Available/Pending</c> increments can never overflow <see cref="long"/>.
/// Pure unit tests — no DB, no fixture. Mirrors the FluentValidation rules with <c>.WithErrorCode(...)</c>.
/// </summary>
public sealed class CreditAmountBoundsTests
{
    private static readonly Guid User = Guid.CreateVersion7();

    private static CreditTopUpCommand TopUp(long amount) =>
        new(User, amount, BucketExpiryDays: null, IdempotencyKey: "key-1");

    private static ReserveCreditsCommand Reserve(long amount) =>
        new(User, amount, HoldMinutes: null);

    [Theory]
    [InlineData(0L)]
    [InlineData(-1L)]
    [InlineData(long.MinValue)]
    public void TopUp_rejects_non_positive_amount(long amount)
    {
        var result = new CreditTopUpValidator().Validate(TopUp(amount));
        HasErrorCode(result, "credit.amount.must_be_positive").ShouldBeTrue();
    }

    [Theory]
    [InlineData(CreditTopUpValidator.MaxAmount + 1)]
    [InlineData(long.MaxValue)]
    public void TopUp_rejects_amount_above_cap(long amount)
    {
        var result = new CreditTopUpValidator().Validate(TopUp(amount));
        HasErrorCode(result, "credit.amount.too_large").ShouldBeTrue();
    }

    [Theory]
    [InlineData(1L)]
    [InlineData(CreditTopUpValidator.MaxAmount)]
    public void TopUp_accepts_amount_within_bounds(long amount)
    {
        var result = new CreditTopUpValidator().Validate(TopUp(amount));
        result.IsValid.ShouldBeTrue();
    }

    [Theory]
    [InlineData(0L)]
    [InlineData(-1L)]
    [InlineData(long.MinValue)]
    public void Reserve_rejects_non_positive_amount(long amount)
    {
        var result = new ReserveCreditsValidator().Validate(Reserve(amount));
        HasErrorCode(result, "credit.amount.must_be_positive").ShouldBeTrue();
    }

    [Theory]
    [InlineData(ReserveCreditsValidator.MaxAmount + 1)]
    [InlineData(long.MaxValue)]
    public void Reserve_rejects_amount_above_cap(long amount)
    {
        var result = new ReserveCreditsValidator().Validate(Reserve(amount));
        HasErrorCode(result, "credit.amount.too_large").ShouldBeTrue();
    }

    [Theory]
    [InlineData(1L)]
    [InlineData(ReserveCreditsValidator.MaxAmount)]
    public void Reserve_accepts_amount_within_bounds(long amount)
    {
        var result = new ReserveCreditsValidator().Validate(Reserve(amount));
        result.IsValid.ShouldBeTrue();
    }

    private static bool HasErrorCode(ValidationResult result, string code) =>
        result.Errors.Exists(e => e.ErrorCode == code);
}
