using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ModularPlatform.Abstractions;
using ModularPlatform.Billing.Persistence;
using ModularPlatform.Billing.Sagas;
using ModularPlatform.Billing.Security;
using ModularPlatform.Billing.Stripe;
using ModularPlatform.Cqrs;
using Wolverine.EntityFrameworkCore;

namespace ModularPlatform.Billing.Features.Packages.PurchaseCreditPackage;

/// <summary>
/// Accept step of the package purchase: creates the Stripe Checkout session and outboxes
/// <see cref="CreditPurchaseStarted"/> — the Worker materializes the <see cref="CreditPurchaseSaga"/>, which
/// owns the rest (confirm → grant → completion event, or abandon timeout). Credits are granted ONLY by the
/// saga through the idempotent top-up; this handler never touches the ledger. Metadata on the session is the
/// single source the webhook router needs (<c>purchase_type=package</c> + the full grant payload).
/// </summary>
internal sealed class PurchaseCreditPackageHandler(
    IDbContextOutbox<BillingDbContext> outbox,
    IStripeGateway gateway,
    ITenantContext tenant,
    IOptions<StripeOptions> stripeOptions)
    : ICommandHandler<PurchaseCreditPackageCommand, PurchaseCreditPackageResponse>
{
    public async Task<PurchaseCreditPackageResponse> Handle(
        PurchaseCreditPackageCommand command, CancellationToken ct)
    {
        var db = outbox.DbContext;

        // A buyer may only purchase a package in its OWN tenant's catalogue (or a platform-global one) — another
        // tenant's package is a 404 (no existence leak), the per-tenant catalogue boundary.
        var callerTenantId = tenant.TenantId;
        var package = await db.CreditPackages.AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == command.PackageId && (p.TenantId == callerTenantId || p.TenantId == null), ct)
            ?? throw new NotFoundException("billing.package_not_found", "Credit package not found.");

        if (!package.Active)
        {
            throw new BusinessRuleException("billing.package_inactive", "The credit package is not for sale.");
        }

        if (string.IsNullOrWhiteSpace(package.StripePriceId))
        {
            throw new BusinessRuleException(
                "billing.package.price_not_configured", "The package has no Stripe price configured.");
        }

        var purchaseId = Guid.CreateVersion7();
        var stripe = stripeOptions.Value;

        var metadata = new Dictionary<string, string>
        {
            ["purchase_type"] = "package",
            ["purchase_id"] = purchaseId.ToString(),
            ["user_id"] = command.UserId.ToString(),
            ["package_id"] = package.Id.ToString(),
            ["credit_amount"] = package.CreditAmount.ToString(CultureInfo.InvariantCulture),
        };
        // Stamp the caller's tenant so the SYSTEM Worker can resolve it from the session metadata at grant time.
        if (tenant.TenantId is { } tenantId)
        {
            metadata["tenant_id"] = tenantId.ToString();
        }
        if (package.BucketExpiryDays is { } expiry)
        {
            metadata["bucket_expiry_days"] = expiry.ToString(CultureInfo.InvariantCulture);
        }

        var session = await gateway.CreateCheckoutSessionAsync(new CheckoutSessionSpec(
            Mode: "payment",
            PriceId: package.StripePriceId,
            ClientReferenceId: purchaseId.ToString(),
            Metadata: metadata,
            AutomaticTax: stripe.AutomaticTax,
            AllowPromotionCodes: stripe.AllowPromotionCodes,
            SuccessUrl: stripe.SuccessUrl,
            CancelUrl: stripe.CancelUrl), ct);

        await outbox.PublishAsync(new CreditPurchaseStarted(
            Id: purchaseId,
            UserId: command.UserId,
            PackageId: package.Id,
            CheckoutSessionId: session.SessionId,
            CreditAmount: package.CreditAmount,
            BucketExpiryDays: package.BucketExpiryDays,
            TimeoutMinutes: stripe.CheckoutTimeoutMinutes));

        await outbox.SaveChangesAndFlushMessagesAsync();

        return new PurchaseCreditPackageResponse(purchaseId, session.SessionId, session.Url);
    }
}
