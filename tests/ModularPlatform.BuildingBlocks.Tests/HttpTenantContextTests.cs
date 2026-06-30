using System.Net;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using ModularPlatform.Web;
using Shouldly;

namespace ModularPlatform.BuildingBlocks.Tests;

public sealed class HttpTenantContextTests
{
    [Fact]
    public void No_http_context_is_system_context_for_in_api_background_work()
    {
        var tenant = new HttpTenantContext(new HttpContextAccessor());

        tenant.IsSystem.ShouldBeTrue();
        tenant.UserId.ShouldBeNull();
        tenant.TenantId.ShouldBeNull();
        tenant.IpAddress.ShouldBeNull();
    }

    [Fact]
    public void Anonymous_http_context_is_not_system_and_does_not_bypass_tenant_filters()
    {
        var accessor = new HttpContextAccessor
        {
            HttpContext = new DefaultHttpContext(),
        };
        var tenant = new HttpTenantContext(accessor);

        tenant.IsSystem.ShouldBeFalse();
        tenant.UserId.ShouldBeNull();
        tenant.TenantId.ShouldBeNull();
    }

    [Fact]
    public void Authenticated_http_context_reads_user_tenant_and_ip_from_the_current_request()
    {
        var userId = Guid.CreateVersion7();
        var tenantId = Guid.CreateVersion7();
        var context = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(
            [
                new Claim(ClaimTypes.NameIdentifier, userId.ToString()),
                new Claim(HttpTenantContext.TenantClaim, tenantId.ToString()),
            ], authenticationType: "test")),
        };
        context.Connection.RemoteIpAddress = IPAddress.Parse("203.0.113.42");
        var tenant = new HttpTenantContext(new HttpContextAccessor { HttpContext = context });

        tenant.IsSystem.ShouldBeFalse();
        tenant.UserId.ShouldBe(userId);
        tenant.TenantId.ShouldBe(tenantId);
        tenant.IpAddress.ShouldBe("203.0.113.42");
    }
}
