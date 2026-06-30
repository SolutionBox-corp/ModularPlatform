using System.Net;
using ModularPlatform.IntegrationTesting;
using Shouldly;

namespace ModularPlatform.Crm.Tests;

/// <summary>
/// Cross-module provisioning: registering a user publishes <c>UserRegistered</c>; the CRM Worker handler
/// (<c>ProvisionCrmWorkspace</c>) seeds exactly one starter task, tenant-stamped so the owner can actually see it
/// through the tenant query filter. Proves the consumer runs AND that the shadow <c>TenantId</c> is stamped (a
/// tenant-less row would be hidden), and that re-provisioning never seeds a second task.
/// </summary>
[Collection("Integration")]
public sealed class CrmWorkspaceProvisioningTests(PlatformApiFactory fixture)
{
    private static string Email() => $"crm-{Guid.CreateVersion7():N}@x.com";

    [Fact]
    public async Task Registering_a_user_seeds_one_starter_task_visible_to_the_owner()
    {
        var (userId, token) = await fixture.RegisterAndLoginAsync(Email(), "Sup3rSecret!");

        // The Worker handles UserRegistered asynchronously — wait for the seeded row (admin connection, bypasses RLS).
        await fixture.WaitForCountAsync($"SELECT count(*) FROM crm_tasks WHERE \"UserId\" = '{userId}'", 1);

        // It must be visible THROUGH the tenant query filter — proves TenantId was stamped (a null tenant would hide it).
        var list = await fixture.Client.SendAsync(fixture.Authed(HttpMethod.Get, "/v1/crm/tasks", token));
        list.StatusCode.ShouldBe(HttpStatusCode.OK);
        var items = (await PlatformApiFactory.ReadData(list)).GetProperty("items").EnumerateArray().ToList();
        items.ShouldContain(t => t.GetProperty("title").GetString() == "Add your first contact");
    }

    [Fact]
    public async Task Provisioning_seeds_exactly_one_starter_task()
    {
        var (userId, _) = await fixture.RegisterAndLoginAsync(Email(), "Sup3rSecret!");
        await fixture.WaitForCountAsync($"SELECT count(*) FROM crm_tasks WHERE \"UserId\" = '{userId}'", 1);

        // Allow any redelivery/retry to settle, then assert the idempotency guard kept it to a single starter task.
        await Task.Delay(500);
        var count = await fixture.ScalarAsync<long>($"SELECT count(*) FROM crm_tasks WHERE \"UserId\" = '{userId}'");
        count.ShouldBe(1);
    }
}
