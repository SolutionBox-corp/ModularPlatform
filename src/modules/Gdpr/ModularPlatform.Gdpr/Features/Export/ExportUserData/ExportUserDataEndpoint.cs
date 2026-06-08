using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using ModularPlatform.Abstractions;
using ModularPlatform.Cqrs;
using ModularPlatform.Web;

namespace ModularPlatform.Gdpr.Features.Export.ExportUserData;

internal static class ExportUserDataEndpoint
{
    public static void MapExportUserData(this IEndpointRouteBuilder app)
    {
        // Self-service: the subject is the AUTHENTICATED user (from the token), never a client-supplied id —
        // a route id would let any logged-in user export anyone's data (IDOR).
        app.MapGet("/gdpr/me/export", async (
                ITenantContext tenant,
                IDispatcher dispatcher,
                CancellationToken ct) =>
            {
                var userId = tenant.UserId ?? throw new UnauthorizedException("auth.required", "Authentication required.");
                var document = await dispatcher.Query(new ExportUserDataQuery(userId), ct);
                return Results.Ok(ApiResponse<Dictionary<string, object?>>.Ok(document));
            })
            .RequireAuthorization()
            .WithTags("Gdpr")
            .WithName("ExportUserData");
    }
}
