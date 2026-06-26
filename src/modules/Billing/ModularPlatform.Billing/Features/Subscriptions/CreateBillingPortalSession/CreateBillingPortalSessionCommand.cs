using ModularPlatform.Cqrs;

namespace ModularPlatform.Billing.Features.Subscriptions.CreateBillingPortalSession;

/// <summary>
/// Opens the Stripe Customer Portal for the caller — the hosted page where they manage payment methods and view /
/// download past invoices &amp; receipts. The Stripe customer id is taken from the caller's subscription rows (the
/// only place a customer id is persisted), NEVER the request. Returns the portal URL for a browser redirect.
/// </summary>
public sealed record CreateBillingPortalSessionCommand(Guid UserId)
    : ICommand<CreateBillingPortalSessionResponse>;

public sealed record CreateBillingPortalSessionResponse(string Url);
