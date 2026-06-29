using System.Net;
using Microsoft.Extensions.DependencyInjection;
using ModularPlatform.Abstractions;
using ModularPlatform.IntegrationTesting;
using Shouldly;

namespace ModularPlatform.Gdpr.Tests;

/// <summary>
/// GD-4 export resilience — per-exporter try/catch: a throwing IExportPersonalData produces
/// <c>{"error":"export_failed"}</c> for THAT module while all other modules export normally.
/// Tests against the real HTTP endpoint with a derived WebApplicationFactory that injects a
/// throwing exporter via an additional scoped registration.
/// </summary>
[Collection("Integration")]
public sealed class ExportResilienceTests(PlatformApiFactory fixture)
{
    private const string Password = "Sup3rSecret!";

    [Fact]
    public async Task Export_endpoint_returns_200_with_error_marker_when_one_exporter_throws()
    {
        var email = $"export-resil-{Guid.CreateVersion7():N}@example.com";
        var (userId, accessToken) = await fixture.RegisterAndLoginAsync(email, Password);

        // Wait for the Billing account (ensures all real exporters have their data ready).
        await fixture.WaitForCountAsync(
            $"SELECT count(*)::bigint FROM credit_accounts WHERE \"UserId\" = '{userId}'", 1);

        using var brokenHost = fixture.CreateHost()
            .WithWebHostBuilder(builder =>
                builder.ConfigureServices(services =>
                    services.AddScoped<IExportPersonalData, BrokenHttpExporter>()));
        using var brokenClient = brokenHost.CreateClient();

        var response = await brokenClient.SendAsync(
            fixture.Authed(HttpMethod.Get, "/v1/gdpr/me/export", accessToken));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var data = await PlatformApiFactory.ReadData(response);

        // All three real module sections must be present.
        data.TryGetProperty("Identity", out _).ShouldBeTrue();
        data.TryGetProperty("Billing", out _).ShouldBeTrue();
        data.TryGetProperty("Notifications", out _).ShouldBeTrue();
        data.TryGetProperty("BrokenHttp", out var broken).ShouldBeTrue();
        broken.GetProperty("error").GetString().ShouldBe("export_failed");
    }

    private sealed class BrokenHttpExporter : IExportPersonalData
    {
        public string ModuleName => "BrokenHttp";

        public Task<IReadOnlyDictionary<string, object?>> ExportAsync(Guid userId, CancellationToken ct) =>
            throw new InvalidOperationException("boom");
    }
}

/// <summary>
/// GD-4 — the resilience half asserted DIRECTLY: one exporter throws, the document still contains the
/// healthy module's data plus an <c>{"error":"export_failed"}</c> marker for the broken one. Unit-level on
/// the internal handler (InternalsVisibleTo) — no host plumbing needed to prove the isolation contract.
/// </summary>
public sealed class ExportResilienceUnitTests
{
    [Fact]
    public async Task A_throwing_exporter_yields_an_error_marker_and_does_not_break_the_others()
    {
        var handler = new ModularPlatform.Gdpr.Features.Export.ExportUserData.ExportUserDataHandler(
            [new HealthyExporter(), new BrokenExporter()],
            Microsoft.Extensions.Logging.Abstractions.NullLogger<
                ModularPlatform.Gdpr.Features.Export.ExportUserData.ExportUserDataHandler>.Instance);

        var document = await handler.Handle(
            new ModularPlatform.Gdpr.Features.Export.ExportUserData.ExportUserDataQuery(Guid.CreateVersion7()),
            CancellationToken.None);

        document["Healthy"].ShouldBeAssignableTo<IReadOnlyDictionary<string, object?>>();
        var broken = document["Broken"].ShouldBeAssignableTo<Dictionary<string, string>>()!;
        broken["error"].ShouldBe("export_failed");
    }

    private sealed class HealthyExporter : ModularPlatform.Abstractions.IExportPersonalData
    {
        public string ModuleName => "Healthy";

        public Task<IReadOnlyDictionary<string, object?>> ExportAsync(Guid userId, CancellationToken ct) =>
            Task.FromResult<IReadOnlyDictionary<string, object?>>(
                new Dictionary<string, object?> { ["ok"] = true });
    }

    private sealed class BrokenExporter : ModularPlatform.Abstractions.IExportPersonalData
    {
        public string ModuleName => "Broken";

        public Task<IReadOnlyDictionary<string, object?>> ExportAsync(Guid userId, CancellationToken ct) =>
            throw new InvalidOperationException("boom");
    }
}
