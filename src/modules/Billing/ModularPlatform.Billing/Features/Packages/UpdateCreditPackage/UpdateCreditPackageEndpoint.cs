using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using ModularPlatform.Abstractions;
using ModularPlatform.Cqrs;
using ModularPlatform.Web;

namespace ModularPlatform.Billing.Features.Packages.UpdateCreditPackage;

internal static class UpdateCreditPackageEndpoint
{
    public static void MapUpdateCreditPackage(this IEndpointRouteBuilder app)
    {
        app.MapPut("/billing/admin/packages/{packageId:guid}", async (
                Guid packageId,
                UpdateCreditPackageRequest request,
                IDispatcher dispatcher,
                CancellationToken ct) =>
            {
                var result = await dispatcher.Send(new UpdateCreditPackageCommand(
                    packageId, request.Name, request.CreditAmount, request.Price,
                    request.BucketExpiryDays, request.Active, request.StripePriceId), ct);
                return Results.Ok(ApiResponse<UpdateCreditPackageResponse>.Ok(result));
            })
            .RequireAuthorization()
            .RequirePermission(PlatformPermissions.BillingManage)
            .WithTags("Billing")
            .WithName("UpdateCreditPackage");
    }
}
