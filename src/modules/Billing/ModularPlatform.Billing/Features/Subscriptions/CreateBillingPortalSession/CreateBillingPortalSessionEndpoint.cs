using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using ModularPlatform.Abstractions;
using ModularPlatform.Cqrs;
using ModularPlatform.Web;

namespace ModularPlatform.Billing.Features.Subscriptions.CreateBillingPortalSession;

internal static class CreateBillingPortalSessionEndpoint
{
    public static void MapCreateBillingPortalSession(this IEndpointRouteBuilder app)
    {
        app.MapPost("/billing/portal", async (
                ITenantContext tenant,
                IDispatcher dispatcher,
                CancellationToken ct) =>
            {
                var userId = tenant.UserId
                    ?? throw new UnauthorizedException("auth.required", "Authentication required.");
                var result = await dispatcher.Send(new CreateBillingPortalSessionCommand(userId), ct);
                return Results.Ok(ApiResponse<CreateBillingPortalSessionResponse>.Ok(result));
            })
            .RequireAuthorization()
            .RequireModule("billing")
            .DenyMachinePrincipals()
            .WithTags("Billing")
            .WithName("CreateBillingPortalSession");
    }
}
