using System.Net;
using ModularPlatform.IntegrationTesting;
using Shouldly;

namespace ModularPlatform.Marketing.Tests;

/// <summary>
/// Phase 1 smoke test: the Marketing module boots inside the shared harness and its migration applies cleanly (the
/// factory runs every enabled module's migrations on startup — a broken <c>marketing</c> schema would fail the whole
/// container here). Endpoint-level pull/analysis tests arrive with Phase 2/3.
/// </summary>
[Collection("Integration")]
public sealed class MarketingModuleTests(PlatformApiFactory fixture)
{
    [Fact]
    public void Module_has_stable_name() => new MarketingModule().Name.ShouldBe("Marketing");

    [Fact]
    public async Task Platform_with_marketing_enabled_boots_and_is_ready()
    {
        var ready = await fixture.Client.GetAsync("/health/ready");
        ready.StatusCode.ShouldBe(HttpStatusCode.OK);

        // A user can still register + log in (the marketing migration didn't break the boot/auth path).
        var (_, token) = await fixture.RegisterAndLoginAsync($"mkt-{Guid.CreateVersion7():N}@x.com", "Sup3rSecret!");
        token.ShouldNotBeNullOrEmpty();
    }
}
