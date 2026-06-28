using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using ModularPlatform.Abstractions;
using ModularPlatform.Cqrs;
using ModularPlatform.Web;

namespace ModularPlatform.Files.Features.Links.UnlinkFile;

internal static class UnlinkFileEndpoint
{
    public static void MapUnlinkFile(this IEndpointRouteBuilder app)
    {
        app.MapDelete("/files/links/{linkId:guid}", async (
                Guid linkId,
                ITenantContext tenant,
                IDispatcher dispatcher,
                CancellationToken ct) =>
            {
                var userId = tenant.UserId
                    ?? throw new UnauthorizedException("auth.required", "Authentication required.");
                await dispatcher.Send(new UnlinkFileCommand(linkId, userId), ct);
                return Results.NoContent();
            })
            .RequireAuthorization()
            .RequireModule("files")
            .WithTags("Files")
            .WithName("UnlinkFile");
    }
}
