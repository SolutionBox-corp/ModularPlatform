using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using ModularPlatform.Cqrs;
using ModularPlatform.Web;

namespace ModularPlatform.Gdpr.Features.Consents.GrantConsent;

internal static class GrantConsentEndpoint
{
    public static void MapGrantConsent(this IEndpointRouteBuilder app)
    {
        app.MapPost("/gdpr/consents/grant", async (
                GrantConsentRequest request,
                IDispatcher dispatcher,
                CancellationToken ct) =>
            {
                var result = await dispatcher.Send(
                    new GrantConsentCommand(request.UserId, request.ConsentType), ct);
                return Results.Ok(ApiResponse<GrantConsentResponse>.Ok(result));
            })
            .RequireAuthorization()
            .WithTags("Gdpr")
            .WithName("GrantConsent");
    }
}
