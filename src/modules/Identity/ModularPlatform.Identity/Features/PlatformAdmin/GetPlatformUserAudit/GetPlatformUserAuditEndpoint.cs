using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using ModularPlatform.Abstractions;
using ModularPlatform.Cqrs;
using ModularPlatform.Identity.Features.Audit.GetUserAuditTrail;
using ModularPlatform.Web;

namespace ModularPlatform.Identity.Features.PlatformAdmin.GetPlatformUserAudit;

/// <summary>
/// Platform-admin CROSS-TENANT forensic read of a user's Identity audit trail with PII decrypted.
/// Reuses the existing <see cref="GetUserAuditTrailQuery"/> — the <c>{module}_audit_entries</c> table carries
/// NO tenant/soft-delete query filter (audit rows are plain, not <c>ITenantScoped</c>/<c>ISoftDeletable</c>),
/// so that handler is already cross-tenant; this slice exposes it under the <c>/identity/platform</c> route.
/// Gated by the same <c>audit.read</c> permission.
/// </summary>
internal static class GetPlatformUserAuditEndpoint
{
    public static void MapGetPlatformUserAudit(this IEndpointRouteBuilder app)
    {
        app.MapGet("/identity/platform/users/{userId:guid}/audit", async (
                Guid userId,
                IDispatcher dispatcher,
                CancellationToken ct) =>
            {
                var trail = await dispatcher.Query(new GetUserAuditTrailQuery(userId, CrossTenant: true), ct);
                return Results.Ok(ApiResponse<UserAuditTrailResponse>.Ok(trail));
            })
            .RequirePermission(PlatformPermissions.AuditRead)
            .WithTags("Identity")
            .WithName("GetPlatformUserAudit");
    }
}
