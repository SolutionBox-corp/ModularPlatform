using System.Net;
using ModularPlatform.IntegrationTesting;
using Shouldly;

namespace ModularPlatform.Billing.Tests;

/// <summary>
/// Host-level smoke test (uses the shared full-API fixture): liveness is always up; readiness reports 200 only
/// when the Postgres dependency is reachable (it is, via the test container).
/// </summary>
public sealed class HealthCheckTests(PlatformApiFactory fixture) : IClassFixture<PlatformApiFactory>
{
    [Fact]
    public async Task Liveness_and_readiness_report_healthy_when_dependencies_are_up()
    {
        (await fixture.Client.GetAsync("/health/live")).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await fixture.Client.GetAsync("/health/ready")).StatusCode.ShouldBe(HttpStatusCode.OK);
    }
}
