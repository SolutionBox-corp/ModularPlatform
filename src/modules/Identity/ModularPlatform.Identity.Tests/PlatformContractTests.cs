using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ModularPlatform.IntegrationTesting;
using Shouldly;

namespace ModularPlatform.Identity.Tests;

/// <summary>
/// Cross-cutting platform contracts (docs/test-scenarios.md):
/// PL-3 (RFC 9457 + stable errorCode + Accept-Language localized detail), PL-7 down-case (/health/ready 503
/// when the DB is unreachable, /health/live still 200), PL-8 (OpenAPI gated outside Development),
/// PL-9 (429 + Retry-After on a low-limit host) and ST-5 (the Stripe webhook is rate-limit exempt).
/// Derived hosts share the fixture's container (the one-host-per-process protector invariant).
/// </summary>
[Collection("Integration")]
public sealed class PlatformContractTests(PlatformApiFactory fixture)
{
    [Fact]
    public async Task PL3_domain_errors_are_rfc9457_with_stable_code_and_localized_detail()
    {
        var payload = new { email = $"nobody-{Guid.CreateVersion7():N}@test.io", password = "wrong-password" };

        var en = await fixture.Client.PostAsJsonAsync("/v1/identity/auth/login", payload);
        en.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        en.Content.Headers.ContentType!.MediaType.ShouldBe("application/problem+json");
        var enBody = JsonDocument.Parse(await en.Content.ReadAsStringAsync()).RootElement;
        enBody.GetProperty("errorCode").GetString().ShouldBe("auth.invalid_credentials");
        var enDetail = enBody.GetProperty("detail").GetString()!;

        var csRequest = new HttpRequestMessage(HttpMethod.Post, "/v1/identity/auth/login")
        {
            Content = JsonContent.Create(payload),
        };
        csRequest.Headers.TryAddWithoutValidation("Accept-Language", "cs");
        var cs = await fixture.Client.SendAsync(csRequest);
        var csBody = JsonDocument.Parse(await cs.Content.ReadAsStringAsync()).RootElement;
        csBody.GetProperty("errorCode").GetString().ShouldBe("auth.invalid_credentials");
        // Same stable code, different (localized) human detail.
        csBody.GetProperty("detail").GetString().ShouldNotBe(enDetail);
    }

    // PL-7 down-case (ready 503 when Postgres dies) is NOT coverable here: a host with an unreachable DB
    // never finishes startup (Wolverine persistence + seeders need the database), so there is no running
    // /health/ready to probe. It needs an ops-level test that kills the DB under a RUNNING host.

    [Fact]
    public async Task PL8_openapi_is_not_served_anonymously_outside_development()
    {
        using var production = fixture.CreateHost(("environment", "Production"));
        using var client = production.CreateClient();

        (await client.GetAsync("/openapi/v1.json")).StatusCode.ShouldNotBe(HttpStatusCode.OK);

        // The shared host runs in Development, where the document IS served.
        (await fixture.Client.GetAsync("/openapi/v1.json")).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task PL9_ST5_low_limit_host_throttles_normal_traffic_but_never_the_stripe_webhook()
    {
        using var lowLimit = fixture.CreateHost(
            ("RateLimiting:GlobalPermitsPerMinute", "5"),
            ("RateLimiting:AuthPermitsPerMinute", "5"));
        using var client = lowLimit.CreateClient();

        // PL-9: anonymous traffic from one IP partition exhausts the 5-permit budget -> 429.
        var statuses = new List<HttpStatusCode>();
        for (var i = 0; i < 12; i++)
        {
            statuses.Add((await client.GetAsync("/v1/billing/packages")).StatusCode);
        }

        statuses.ShouldContain(HttpStatusCode.TooManyRequests);

        // ST-5: the Stripe webhook opted out of rate limiting — bursts never see 429 (bad signature -> 400,
        // which proves the request REACHED the endpoint instead of being throttled).
        for (var i = 0; i < 30; i++)
        {
            var webhook = new HttpRequestMessage(HttpMethod.Post, "/v1/billing/webhooks/stripe")
            {
                Content = new StringContent("{}", System.Text.Encoding.UTF8, "application/json"),
            };
            webhook.Headers.TryAddWithoutValidation(
                "Stripe-Signature", $"t={DateTimeOffset.UtcNow.ToUnixTimeSeconds()},v1={new string('0', 64)}");
            var response = await client.SendAsync(webhook);
            response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        }
    }
}
