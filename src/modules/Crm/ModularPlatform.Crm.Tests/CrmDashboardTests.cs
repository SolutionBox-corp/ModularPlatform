using System.Net;
using ModularPlatform.IntegrationTesting;
using Shouldly;

namespace ModularPlatform.Crm.Tests;

[Collection("Integration")]
public sealed class CrmDashboardTests(PlatformApiFactory fixture)
{
    private static string Email() => $"crm-dashboard-{Guid.CreateVersion7():N}@x.com";

    [Fact]
    public async Task Dashboard_summarizes_pipeline_tasks_and_lead_sources()
    {
        var (_, token) = await fixture.RegisterAndLoginAsync(Email(), "Sup3rSecret!");

        await CreateDeal(token, new
        {
            title = "Proposal", amountCents = 10_000L, stage = "proposal", leadSource = "web",
            expectedCloseAt = DateTimeOffset.UtcNow.AddDays(-1),
        });
        await CreateDeal(token, new
        {
            title = "Won", amountCents = 20_000L, stage = "won", leadSource = "referral",
        });
        await CreateTask(token, new { title = "Overdue", dueAt = DateTimeOffset.UtcNow.AddDays(-1) });

        var dashboard = await fixture.Client.SendAsync(fixture.Authed(HttpMethod.Get, "/v1/crm/dashboard", token));
        dashboard.StatusCode.ShouldBe(HttpStatusCode.OK);
        var data = await PlatformApiFactory.ReadData(dashboard);

        data.GetProperty("openDealsCount").GetInt32().ShouldBe(1);
        data.GetProperty("wonDealsCount").GetInt32().ShouldBe(1);
        data.GetProperty("overdueDealsCount").GetInt32().ShouldBe(1);
        data.GetProperty("openTasksCount").GetInt32().ShouldBeGreaterThanOrEqualTo(1);
        data.GetProperty("overdueTasksCount").GetInt32().ShouldBe(1);
        data.GetProperty("stages").EnumerateArray().ShouldContain(row => row.GetProperty("stage").GetString() == "proposal");
        data.GetProperty("leadSources").EnumerateArray().ShouldContain(row => row.GetProperty("leadSource").GetString() == "web");
    }

    private async Task CreateDeal(string token, object body)
    {
        var resp = await fixture.Client.SendAsync(fixture.Authed(HttpMethod.Post, "/v1/crm/deals", token, body));
        resp.StatusCode.ShouldBe(HttpStatusCode.Created, await resp.Content.ReadAsStringAsync());
    }

    private async Task CreateTask(string token, object body)
    {
        var resp = await fixture.Client.SendAsync(fixture.Authed(HttpMethod.Post, "/v1/crm/tasks", token, body));
        resp.StatusCode.ShouldBe(HttpStatusCode.Created, await resp.Content.ReadAsStringAsync());
    }
}
