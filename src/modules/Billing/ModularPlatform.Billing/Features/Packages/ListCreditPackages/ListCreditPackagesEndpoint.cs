using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using ModularPlatform.Cqrs;
using ModularPlatform.Web;

namespace ModularPlatform.Billing.Features.Packages.ListCreditPackages;

internal static class ListCreditPackagesEndpoint
{
    public static void MapListCreditPackages(this IEndpointRouteBuilder app)
    {
        app.MapGet("/billing/packages", async (IDispatcher dispatcher, CancellationToken ct) =>
            {
                var packages = await dispatcher.Query(new ListCreditPackagesQuery(), ct);
                return Results.Ok(ApiResponse<IReadOnlyList<CreditPackageResponse>>.Ok(packages));
            })
            .RequireAuthorization()
            .RequireModule("billing")
            .WithTags("Billing")
            .WithName("ListCreditPackages");
    }
}
