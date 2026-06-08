using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using ModularPlatform.Cqrs;
using ModularPlatform.Web;

namespace ModularPlatform.Gdpr.Features.Consents.WithdrawConsent;

internal static class WithdrawConsentEndpoint
{
    public static void MapWithdrawConsent(this IEndpointRouteBuilder app)
    {
        app.MapPost("/gdpr/consents/withdraw", async (
                WithdrawConsentRequest request,
                IDispatcher dispatcher,
                CancellationToken ct) =>
            {
                var result = await dispatcher.Send(
                    new WithdrawConsentCommand(request.UserId, request.ConsentType), ct);
                return Results.Ok(ApiResponse<WithdrawConsentResponse>.Ok(result));
            })
            .RequireAuthorization()
            .WithTags("Gdpr")
            .WithName("WithdrawConsent");
    }
}
