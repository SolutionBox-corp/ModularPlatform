using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using ModularPlatform.Abstractions;
using ModularPlatform.Cqrs;
using ModularPlatform.Web;

namespace ModularPlatform.Operations.Features.Status;

/// <summary>The status-polling half of the 202 pattern. Owner-scoped by RLS; returns 404 for anyone but the owner.</summary>
internal static class GetOperationStatusEndpoint
{
    public static void MapGetOperationStatus(this IEndpointRouteBuilder app)
    {
        app.MapGet("/operations/{operationId:guid}", async (
                Guid operationId,
                ITenantContext tenant,
                IDispatcher dispatcher,
                CancellationToken ct) =>
            {
                var userId = tenant.UserId
                    ?? throw new UnauthorizedException("auth.required", "Authentication required.");
                var result = await dispatcher.Query(new GetOperationStatusQuery(operationId, userId), ct);
                return Results.Ok(ApiResponse<OperationStatusResponse>.Ok(result));
            })
            .RequireAuthorization()
            .WithTags("Operations")
            .WithName("GetOperationStatus");
    }
}
