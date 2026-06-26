using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using ModularPlatform.Abstractions;
using ModularPlatform.Cqrs;
using ModularPlatform.Web;

namespace ModularPlatform.Billing.Features.Packages.ListAdminCreditPackages;

/// <summary>
/// Admin catalogue listing (active + inactive). Gated by <c>billing.manage</c> ONLY — NOT <c>RequireModule</c>:
/// managing the catalogue is a permission, not a per-tenant feature entitlement, so the SYSTEM platform admin
/// (which has no tenant and would 404 on the entitlement guard) can manage the global catalogue.
/// </summary>
internal static class ListAdminCreditPackagesEndpoint
{
    public static void MapListAdminCreditPackages(this IEndpointRouteBuilder app)
    {
        app.MapGet("/billing/admin/packages", async (
                int? page,
                int? pageSize,
                IDispatcher dispatcher,
                CancellationToken ct) =>
            {
                var result = await dispatcher.Query(
                    new ListAdminCreditPackagesQuery(new PageRequest(page, pageSize)), ct);
                return Results.Ok(ApiResponse<PagedResponse<AdminCreditPackageResponse>>.Ok(result));
            })
            .RequireAuthorization()
            .RequirePermission(PlatformPermissions.BillingManage)
            .WithTags("Billing")
            .WithName("ListAdminCreditPackages");
    }
}
