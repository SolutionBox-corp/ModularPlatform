using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using ModularPlatform.Abstractions;
using ModularPlatform.Cqrs;
using ModularPlatform.Web;

namespace ModularPlatform.Identity.Features.Audit.GetUserAuditTrail;

/// <summary>
/// Admin forensic read of a user's Identity audit trail with PII decrypted. Gated by <c>audit.read</c>; the target
/// user id is a ROUTE id (an admin operation over another subject, like role assignment) — the permission, not the
/// token subject, is the authorization.
/// </summary>
internal static class GetUserAuditTrailEndpoint
{
    public static void MapGetUserAuditTrail(this IEndpointRouteBuilder app)
    {
        app.MapGet("/identity/admin/users/{userId:guid}/audit", async (
                Guid userId,
                IDispatcher dispatcher,
                CancellationToken ct) =>
            {
                var trail = await dispatcher.Query(new GetUserAuditTrailQuery(userId), ct);
                return Results.Ok(ApiResponse<UserAuditTrailResponse>.Ok(trail));
            })
            .RequirePermission(PlatformPermissions.AuditRead)
            .WithTags("Identity")
            .WithName("GetUserAuditTrail");
    }
}
