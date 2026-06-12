using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using ModularPlatform.Abstractions;
using ModularPlatform.Cqrs;
using ModularPlatform.Web;

namespace ModularPlatform.Billing.Features.Packages.PurchaseCreditPackage;

internal static class PurchaseCreditPackageEndpoint
{
    public static void MapPurchaseCreditPackage(this IEndpointRouteBuilder app)
    {
        app.MapPost("/billing/packages/{packageId:guid}/checkout", async (
                Guid packageId,
                ITenantContext tenant,
                IDispatcher dispatcher,
                CancellationToken ct) =>
            {
                var userId = tenant.UserId
                    ?? throw new UnauthorizedException("auth.required", "Authentication required.");
                var result = await dispatcher.Send(new PurchaseCreditPackageCommand(userId, packageId), ct);
                return Results.Ok(ApiResponse<PurchaseCreditPackageResponse>.Ok(result));
            })
            .RequireAuthorization()
            .RequireModule("billing")
            .WithTags("Billing")
            .WithName("PurchaseCreditPackage");
    }
}
