using Microsoft.Extensions.DependencyInjection;
using ModularPlatform.Cqrs;
using ModularPlatform.IntegrationTesting;
using ModularPlatform.Marketing.Features.TenantSnapshots.ReconcileTenantSnapshots;
using ModularPlatform.Marketing.Features.TenantSnapshots.UpsertTenantSnapshot;
using Shouldly;
using System.Net;
using System.Net.Http.Json;

namespace ModularPlatform.Marketing.Tests;

/// <summary>
/// UC96: canonical local projection of another module's data. Tenancy owns tenant registry data; Marketing keeps a
/// small repairable snapshot fed by public contracts/ports, not by a cross-module Core reference or SQL join.
/// </summary>
[Collection("Integration")]
public sealed class TenantSnapshotProjectionTests(PlatformApiFactory fixture)
{
    private const string Password = "Sup3rSecret!";

    [Fact]
    public async Task Tenant_provisioned_event_creates_a_marketing_tenant_snapshot()
    {
        var email = $"mkt-tenant-projection-{Guid.CreateVersion7():N}@example.com";
        await fixture.RegisterAndLoginAsync(email, Password);
        var tenantId = await TenantIdForEmailAsync(email);

        await fixture.WaitForCountAsync(
            $"""SELECT count(*)::bigint FROM marketing_tenant_snapshots WHERE "TenantId" = '{tenantId}'""", 1);

        var name = await fixture.ScalarAsync<string>(
            $"""SELECT "Name" FROM marketing_tenant_snapshots WHERE "TenantId" = '{tenantId}'""");
        name.ShouldStartWith("tenant-");
    }

    [Fact]
    public async Task Tenant_updated_event_refreshes_the_marketing_tenant_snapshot()
    {
        var email = $"mkt-tenant-update-{Guid.CreateVersion7():N}@example.com";
        await fixture.RegisterAndLoginAsync(email, Password);
        var tenantId = await TenantIdForEmailAsync(email);

        await fixture.WaitForCountAsync(
            $"""SELECT count(*)::bigint FROM marketing_tenant_snapshots WHERE "TenantId" = '{tenantId}'""", 1);

        var admin = await AdminTokenAsync();
        var subdomain = $"mkt-upd-{Guid.CreateVersion7():N}"[..30];
        var update = await fixture.Client.SendAsync(fixture.Authed(
            HttpMethod.Put,
            $"/v1/tenant/admin/tenants/{tenantId}",
            admin,
            new { name = "Marketing Snapshot Updated", subdomain }));
        update.StatusCode.ShouldBe(HttpStatusCode.OK);

        await fixture.WaitForCountAsync(
            $"""SELECT count(*)::bigint FROM marketing_tenant_snapshots WHERE "TenantId" = '{tenantId}' AND "Subdomain" = '{subdomain}' AND "Name" = 'Marketing Snapshot Updated'""",
            1);
    }

    [Fact]
    public async Task Snapshot_upsert_is_idempotent_and_ignores_older_events()
    {
        var tenantId = Guid.CreateVersion7();
        var newer = DateTimeOffset.Parse("2026-06-28T09:00:00Z");
        var older = DateTimeOffset.Parse("2026-06-28T08:00:00Z");

        using var scope = fixture.Services.CreateScope();
        var dispatcher = scope.ServiceProvider.GetRequiredService<IDispatcher>();

        await dispatcher.Send(new UpsertTenantSnapshotCommand(
            tenantId,
            "alpha",
            "Alpha tenant",
            Guid.CreateVersion7(),
            newer), CancellationToken.None);

        await dispatcher.Send(new UpsertTenantSnapshotCommand(
            tenantId,
            "alpha-old",
            "Old alpha tenant",
            Guid.CreateVersion7(),
            older), CancellationToken.None);

        await dispatcher.Send(new UpsertTenantSnapshotCommand(
            tenantId,
            "alpha",
            "Alpha tenant",
            Guid.CreateVersion7(),
            newer), CancellationToken.None);

        var projection = await fixture.ScalarAsync<string>(
            $"""SELECT "Subdomain" || ':' || "Name" || ':' || count(*) OVER () FROM marketing_tenant_snapshots WHERE "TenantId" = '{tenantId}'""");
        projection.ShouldBe("alpha:Alpha tenant:1");
    }

    [Fact]
    public async Task Reconcile_repairs_a_missing_snapshot_through_the_tenant_directory_port()
    {
        var email = $"mkt-tenant-reconcile-{Guid.CreateVersion7():N}@example.com";
        await fixture.RegisterAndLoginAsync(email, Password);
        var tenantId = await TenantIdForEmailAsync(email);

        await fixture.ExecuteSqlAsync($"""DELETE FROM marketing_tenant_snapshots WHERE "TenantId" = '{tenantId}'""");

        using var scope = fixture.Services.CreateScope();
        var dispatcher = scope.ServiceProvider.GetRequiredService<IDispatcher>();
        var result = await dispatcher.Send(new ReconcileTenantSnapshotsCommand(tenantId), CancellationToken.None);

        result.Scanned.ShouldBe(1);
        result.Repaired.ShouldBe(1);
        result.Missing.ShouldBe(0);

        var count = await fixture.ScalarAsync<long>(
            $"""SELECT count(*)::bigint FROM marketing_tenant_snapshots WHERE "TenantId" = '{tenantId}'""");
        count.ShouldBe(1);
    }

    private async Task<Guid> TenantIdForEmailAsync(string email)
    {
        var emailHash = PlatformApiFactory.EmailHashOf(email);
        return await fixture.ScalarAsync<Guid>(
            $"""SELECT "TenantId" FROM users WHERE "EmailHash" = '{emailHash}'""");
    }

    private async Task<string> AdminTokenAsync()
    {
        await fixture.Client.PostAsJsonAsync(
            "/v1/identity/users",
            new { email = PlatformApiFactory.AdminEmail, password = Password });
        var login = await fixture.Client.PostAsJsonAsync(
            "/v1/identity/auth/login",
            new { email = PlatformApiFactory.AdminEmail, password = Password });
        login.IsSuccessStatusCode.ShouldBeTrue();
        return (await PlatformApiFactory.ReadData(login)).GetProperty("accessToken").GetString()!;
    }
}
