using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using ModularPlatform.Abstractions;
using ModularPlatform.Cqrs;
using ModularPlatform.Web;

namespace ModularPlatform.Gdpr.Features.Consents.WithdrawConsent;

internal static class WithdrawConsentEndpoint
{
    public static void MapWithdrawConsent(this IEndpointRouteBuilder app)
    {
        app.MapPost("/gdpr/consents/withdraw", async (
                WithdrawConsentRequest request,
                ITenantContext tenant,
                IDispatcher dispatcher,
                CancellationToken ct) =>
            {
                var userId = tenant.UserId ?? throw new UnauthorizedException("auth.required", "Authentication required.");
                var result = await dispatcher.Send(new WithdrawConsentCommand(userId, request.ConsentType), ct);
                return Results.Ok(ApiResponse<WithdrawConsentResponse>.Ok(result));
            })
            .RequireAuthorization()
            .WithTags("Gdpr")
            .WithName("WithdrawConsent");
    }
}
