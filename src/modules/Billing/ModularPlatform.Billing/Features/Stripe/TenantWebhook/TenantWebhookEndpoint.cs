using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Routing;
using ModularPlatform.Cqrs;

namespace ModularPlatform.Billing.Features.Stripe.TenantWebhook;

/// <summary>
/// Per-tenant payment webhook: <c>POST /billing/webhooks/{provider}/{tenantId}</c>. The tenant id in the URL binds the
/// notification to a tenant BEFORE any verification (Stripe HMAC bound to that tenant's secret; GoPay re-fetch with
/// that tenant's credentials), then the handler resolves the tenant's gateway and confirms a paid package purchase.
/// Anonymous + un-throttled (providers retry on non-200); returns 200 immediately and does the work via the dispatcher.
/// </summary>
internal static class TenantWebhookEndpoint
{
    public static void MapTenantWebhook(this IEndpointRouteBuilder app)
    {
        // Optional {token} tail: GoPay callbacks are UNSIGNED, so a per-tenant secret token in the URL is the binding
        // the handler checks (Stripe ignores it — its HMAC signature is the binding). The notification URL the config
        // store hands GoPay includes this token, so the route MUST accept it or every GoPay callback would 404.
        app.MapPost("/billing/webhooks/{provider}/{tenantId:guid}/{token?}", async (
                Guid tenantId,
                string? token,
                HttpRequest request,
                IDispatcher dispatcher,
                CancellationToken ct) =>
            {
                using var reader = new StreamReader(request.Body);
                var rawBody = await reader.ReadToEndAsync(ct);
                var signature = request.Headers["Stripe-Signature"].ToString();
                var query = request.Query.ToDictionary(q => q.Key, q => q.Value.ToString());

                await dispatcher.Send(new ProcessTenantWebhookCommand(tenantId, token, rawBody, signature, query), ct);
                return Results.Ok();
            })
            .AllowAnonymous()
            .DisableRateLimiting()
            .WithTags("Billing")
            .WithName("TenantPaymentWebhook");
    }
}
