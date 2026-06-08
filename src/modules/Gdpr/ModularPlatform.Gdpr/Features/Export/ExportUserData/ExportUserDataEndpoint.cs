using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using ModularPlatform.Cqrs;
using ModularPlatform.Web;

namespace ModularPlatform.Gdpr.Features.Export.ExportUserData;

internal static class ExportUserDataEndpoint
{
    public static void MapExportUserData(this IEndpointRouteBuilder app)
    {
        app.MapGet("/gdpr/users/{id:guid}/export", async (
                Guid id,
                IDispatcher dispatcher,
                CancellationToken ct) =>
            {
                var document = await dispatcher.Query(new ExportUserDataQuery(id), ct);
                return Results.Ok(ApiResponse<Dictionary<string, object?>>.Ok(document));
            })
            .RequireAuthorization()
            .WithTags("Gdpr")
            .WithName("ExportUserData");
    }
}
