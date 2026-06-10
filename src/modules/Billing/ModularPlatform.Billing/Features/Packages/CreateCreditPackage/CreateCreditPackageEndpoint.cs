using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using ModularPlatform.Abstractions;
using ModularPlatform.Cqrs;
using ModularPlatform.Web;

namespace ModularPlatform.Billing.Features.Packages.CreateCreditPackage;

internal static class CreateCreditPackageEndpoint
{
    public static void MapCreateCreditPackage(this IEndpointRouteBuilder app)
    {
        app.MapPost("/billing/admin/packages", async (
                CreateCreditPackageRequest request,
                IDispatcher dispatcher,
                CancellationToken ct) =>
            {
                var result = await dispatcher.Send(new CreateCreditPackageCommand(
                    request.Name, request.CreditAmount, request.Price,
                    request.BucketExpiryDays, request.Active, request.StripePriceId), ct);
                return Results.Ok(ApiResponse<CreateCreditPackageResponse>.Ok(result));
            })
            .RequireAuthorization()
            .RequirePermission(PlatformPermissions.BillingManage)
            .WithTags("Billing")
            .WithName("CreateCreditPackage");
    }
}
