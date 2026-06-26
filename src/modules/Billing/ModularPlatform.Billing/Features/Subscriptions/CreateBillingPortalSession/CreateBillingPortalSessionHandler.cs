using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ModularPlatform.Billing.Persistence;
using ModularPlatform.Billing.Security;
using ModularPlatform.Billing.Stripe;
using ModularPlatform.Cqrs;
using ModularPlatform.Persistence;
using ModularPlatform.Web;
using Stripe;

namespace ModularPlatform.Billing.Features.Subscriptions.CreateBillingPortalSession;

/// <summary>
/// Resolves the caller's Stripe customer id from their subscription rows (the most recent one carrying one — even a
/// canceled subscription's customer can still access past invoices) and creates a Customer Portal session. A user
/// who never started a Stripe flow has no customer id → a business-rule error (nothing to manage yet).
/// </summary>
internal sealed class CreateBillingPortalSessionHandler(
    IReadDbContextFactory<BillingDbContext> readFactory,
    IStripeGateway gateway,
    IOptions<StripeOptions> stripeOptions)
    : ICommandHandler<CreateBillingPortalSessionCommand, CreateBillingPortalSessionResponse>
{
    public async Task<CreateBillingPortalSessionResponse> Handle(
        CreateBillingPortalSessionCommand command, CancellationToken ct)
    {
        await using var db = readFactory.Create();

        var customerId = await db.Subscriptions
            .Where(s => s.UserId == command.UserId && s.StripeCustomerId != null)
            .OrderByDescending(s => s.CreatedAt)
            .Select(s => s.StripeCustomerId)
            .FirstOrDefaultAsync(ct);

        if (string.IsNullOrEmpty(customerId))
        {
            throw new BusinessRuleException(
                "billing.no_billing_account", "No billing account yet. Subscribe or buy credits first.");
        }

        var returnUrl = stripeOptions.Value.SuccessUrl;
        if (!Uri.TryCreate(returnUrl, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp))
        {
            throw new BusinessRuleException(
                "billing.portal.invalid_return_url",
                "Billing portal return URL must be an absolute http(s) URL.");
        }

        string url;
        try
        {
            url = await gateway.CreateBillingPortalSessionAsync(customerId, returnUrl, ct);
        }
        catch (StripeException ex)
        {
            throw new BusinessRuleException(
                "billing.portal.provider_failed",
                $"The billing portal provider rejected the request: {ex.Message}");
        }

        return new CreateBillingPortalSessionResponse(url);
    }
}
