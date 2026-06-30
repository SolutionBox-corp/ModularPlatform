using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using ModularPlatform.Abstractions;
using ModularPlatform.Cqrs;
using ModularPlatform.Web;

namespace ModularPlatform.Gdpr.Features.Consents.GrantConsent;

internal static class GrantConsentEndpoint
{
    public static void MapGrantConsent(this IEndpointRouteBuilder app)
    {
        app.MapPost("/gdpr/consents/grant", async (
                GrantConsentRequest request,
                ITenantContext tenant,
                IDispatcher dispatcher,
                CancellationToken ct) =>
            {
                // Identity comes from the token. The wire request deliberately has no UserId.
                var userId = tenant.UserId ?? throw new UnauthorizedException("auth.required", "Authentication required.");
                var result = await dispatcher.Send(new GrantConsentCommand(userId, request.ConsentType, request.PolicyVersion), ct);
                return Results.Ok(ApiResponse<GrantConsentResponse>.Ok(result));
            })
            .RequireAuthorization()
            .WithTags("Gdpr")
            .WithName("GrantConsent");
    }
}
