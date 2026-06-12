using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using ModularPlatform.Abstractions;
using ModularPlatform.Billing.Persistence;
using ModularPlatform.Billing.Sagas;
using ModularPlatform.Cqrs;
using ModularPlatform.Payments;
using Wolverine.EntityFrameworkCore;

namespace ModularPlatform.Billing.Features.Packages.PurchaseCreditPackage;

/// <summary>
/// Accept step of the package purchase: creates a checkout on the TENANT's OWN gateway (resolved per tenant via
/// <see cref="IPaymentGatewayResolver"/>) and outboxes <see cref="CreditPurchaseStarted"/> — the Worker materializes
/// the <see cref="CreditPurchaseSaga"/>, which owns the rest (confirm → grant → completion, or abandon timeout).
/// Credits are granted ONLY by the saga through the idempotent top-up; this handler never touches the ledger. The
/// checkout metadata (<c>purchase_type=package</c> + the full grant payload + <c>tenant_id</c>) is the single source
/// the per-tenant webhook needs to confirm and to resolve the SYSTEM-context tenant at grant time.
/// </summary>
internal sealed class PurchaseCreditPackageHandler(
    IDbContextOutbox<BillingDbContext> outbox,
    IPaymentGatewayResolver gatewayResolver,
    ITenantContext tenant,
    IConfiguration configuration)
    : ICommandHandler<PurchaseCreditPackageCommand, PurchaseCreditPackageResponse>
{
    public async Task<PurchaseCreditPackageResponse> Handle(
        PurchaseCreditPackageCommand command, CancellationToken ct)
    {
        var db = outbox.DbContext;

        var callerTenantId = tenant.TenantId
            ?? throw new UnauthorizedException("auth.required", "Authentication required.");

        // A buyer may only purchase a package in its OWN tenant's catalogue (or a platform-global one) — another
        // tenant's package is a 404 (no existence leak), the per-tenant catalogue boundary.
        var package = await db.CreditPackages.AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == command.PackageId && (p.TenantId == callerTenantId || p.TenantId == null), ct)
            ?? throw new NotFoundException("billing.package_not_found", "Credit package not found.");

        if (!package.Active)
        {
            throw new BusinessRuleException("billing.package_inactive", "The credit package is not for sale.");
        }

        var purchaseId = Guid.CreateVersion7();

        var metadata = new Dictionary<string, string>
        {
            ["purchase_type"] = "package",
            ["purchase_id"] = purchaseId.ToString(),
            ["user_id"] = command.UserId.ToString(),
            ["package_id"] = package.Id.ToString(),
            ["credit_amount"] = package.CreditAmount.ToString(CultureInfo.InvariantCulture),
            ["tenant_id"] = callerTenantId.ToString(),
        };
        if (package.BucketExpiryDays is { } expiry)
        {
            metadata["bucket_expiry_days"] = expiry.ToString(CultureInfo.InvariantCulture);
        }

        IPaymentGateway gateway;
        try
        {
            gateway = await gatewayResolver.ResolveAsync(callerTenantId, PaymentPlane.Tenant, ct);
        }
        catch (PaymentGatewayUnavailableException ex)
        {
            throw new BusinessRuleException(ex.ErrorCode, ex.Message);
        }

        var checkout = await gateway.CreateCheckoutAsync(new CheckoutRequest(
            ReferenceId: purchaseId.ToString(),
            AmountMinorUnits: CurrencyMinorUnits.ToMinorUnits(package.Price, package.Currency),
            Currency: package.Currency,
            Mode: CheckoutMode.Payment,
            Description: package.Name,
            Metadata: metadata,
            SuccessUrl: configuration["Billing:Payments:SuccessUrl"] ?? "https://app/billing/success",
            CancelUrl: configuration["Billing:Payments:CancelUrl"] ?? "https://app/billing/cancel"), ct);

        await outbox.PublishAsync(new CreditPurchaseStarted(
            Id: purchaseId,
            UserId: command.UserId,
            PackageId: package.Id,
            CheckoutSessionId: checkout.ProviderPaymentId,
            CreditAmount: package.CreditAmount,
            BucketExpiryDays: package.BucketExpiryDays,
            TimeoutMinutes: configuration.GetValue("Billing:Payments:CheckoutTimeoutMinutes", 120)));

        await outbox.SaveChangesAndFlushMessagesAsync();

        return new PurchaseCreditPackageResponse(purchaseId, checkout.ProviderPaymentId, checkout.RedirectUrl);
    }
}
