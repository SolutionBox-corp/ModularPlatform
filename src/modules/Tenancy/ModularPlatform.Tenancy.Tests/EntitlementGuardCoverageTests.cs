using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using ModularPlatform.IntegrationTesting;
using ModularPlatform.Web;
using Shouldly;

namespace ModularPlatform.Tenancy.Tests;

/// <summary>
/// Enforces that EVERY product endpoint of an entitlement-gated module carries the live <c>RequireModule(...)</c>
/// guard — the guard is an endpoint filter (invisible in metadata), so without this test it is trivially forgotten
/// (the gotcha audit found only 2 of ~19 Billing endpoints gated). Anonymous provider webhooks are exempt (no tenant
/// context). GDPR is deliberately NOT gated (a legal right must never 404), so it is not in the gated set.
/// </summary>
[Collection("Integration")]
public sealed class EntitlementGuardCoverageTests(PlatformApiFactory fixture)
{
    private static readonly Dictionary<string, string> GatedPrefixes = new()
    {
        ["/v1/billing/"] = "billing",
        ["/v1/files/"] = "files",
        ["/v1/operations/"] = "operations",
        ["/v1/notifications/"] = "notifications",
    };

    [Fact]
    public void Every_gated_module_endpoint_carries_the_entitlement_guard()
    {
        var source = fixture.Services.GetRequiredService<EndpointDataSource>();
        var violations = new List<string>();
        var checkedCount = 0;

        foreach (var endpoint in source.Endpoints.OfType<RouteEndpoint>())
        {
            var route = "/" + (endpoint.RoutePattern.RawText ?? string.Empty).TrimStart('/');
            var match = GatedPrefixes.FirstOrDefault(p => route.StartsWith(p.Key, StringComparison.OrdinalIgnoreCase));
            if (match.Key is null)
            {
                continue; // not a gated-module route
            }

            // Anonymous provider callbacks (webhooks) have no tenant context — exempt.
            if (endpoint.Metadata.GetMetadata<IAllowAnonymous>() is not null)
            {
                continue;
            }

            // Platform/admin catalogue operations are permission-gated control-plane endpoints, not tenant product
            // usage. A platform admin must be able to manage the catalogue even when its own tenant is not entitled.
            if (route.StartsWith("/v1/billing/admin/", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            checkedCount++;

            var marker = endpoint.Metadata.GetMetadata<ModuleEntitlementMetadata>();
            if (marker is null)
            {
                violations.Add($"{route} (expected RequireModule(\"{match.Value}\"))");
            }
            else if (!string.Equals(marker.ModuleKey, match.Value, StringComparison.Ordinal))
            {
                violations.Add($"{route} gates module '{marker.ModuleKey}' but its route implies '{match.Value}'");
            }
        }

        // Guard against a vacuous pass (e.g. if the route prefix ever changes and nothing matches).
        checkedCount.ShouldBeGreaterThan(15, "expected the gated-module endpoints to be discovered");
        violations.ShouldBeEmpty(
            "These gated-module endpoints are missing/mismatched .RequireModule(...):\n" + string.Join("\n", violations));
    }
}
