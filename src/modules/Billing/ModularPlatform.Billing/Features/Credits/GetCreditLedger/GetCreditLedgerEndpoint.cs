using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using ModularPlatform.Abstractions;
using ModularPlatform.Cqrs;
using ModularPlatform.Web;

namespace ModularPlatform.Billing.Features.Credits.GetCreditLedger;

/// <summary>Paged list of the caller's own credit ledger entries (newest first). Owner from the token.</summary>
internal static class GetCreditLedgerEndpoint
{
    public static void MapGetCreditLedger(this IEndpointRouteBuilder app)
    {
        app.MapGet("/billing/credits/entries", async (
                int? page,
                int? pageSize,
                ITenantContext tenant,
                IDispatcher dispatcher,
                CancellationToken ct) =>
            {
                var userId = tenant.UserId
                    ?? throw new UnauthorizedException("auth.required", "Authentication required.");
                var result = await dispatcher.Query(
                    new GetCreditLedgerQuery(userId, new PageRequest(page, pageSize)), ct);
                return Results.Ok(ApiResponse<PagedResponse<CreditLedgerEntry>>.Ok(result));
            })
            .RequireAuthorization()
            .RequireModule("billing")
            .WithTags("Billing")
            .WithName("GetCreditLedger");
    }
}
