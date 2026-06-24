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
                    request.Name, request.CreditAmount, request.Price, request.Currency,
                    request.BucketExpiryDays, request.Active, request.StripePriceId), ct);
                return Results.Ok(ApiResponse<CreateCreditPackageResponse>.Ok(result));
            })
            // Gated by the billing.manage PERMISSION only — NOT RequireModule. Managing the catalogue is an
            // authorization, not a per-tenant feature entitlement; the SYSTEM platform admin (no tenant) manages the
            // global catalogue (TenantId null), and a tenant admin manages its own — both via billing.manage.
            .RequireAuthorization()
            .RequirePermission(PlatformPermissions.BillingManage)
            .WithTags("Billing")
            .WithName("CreateCreditPackage");
    }
}
