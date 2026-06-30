using System.Net;
using ModularPlatform.IntegrationTesting;
using Shouldly;

namespace ModularPlatform.Crm.Tests;

/// <summary>
/// CRM Tasks/reminders end-to-end: create→get, list filtered by status + due cutoff + owner-scoped, partial patch,
/// idempotent complete, and soft-delete. Foreign ids are 404s (RLS + explicit WHERE).
/// </summary>
[Collection("Integration")]
public sealed class CrmTasksTests(PlatformApiFactory fixture)
{
    private static string Email() => $"crm-{Guid.CreateVersion7():N}@x.com";

    private async Task<Guid> CreateTaskAsync(string token, object body)
    {
        var resp = await fixture.Client.SendAsync(fixture.Authed(HttpMethod.Post, "/v1/crm/tasks", token, body));
        resp.StatusCode.ShouldBe(HttpStatusCode.Created, await resp.Content.ReadAsStringAsync());
        return (await PlatformApiFactory.ReadData(resp)).GetProperty("id").GetGuid();
    }

    [Fact]
    public async Task Create_then_get_round_trips()
    {
        var (_, token) = await fixture.RegisterAndLoginAsync(Email(), "Sup3rSecret!");
        var id = await CreateTaskAsync(token, new { title = "Call back", priority = "high" });

        var get = await fixture.Client.SendAsync(fixture.Authed(HttpMethod.Get, $"/v1/crm/tasks/{id}", token));
        get.StatusCode.ShouldBe(HttpStatusCode.OK);
        var data = await PlatformApiFactory.ReadData(get);
        data.GetProperty("title").GetString().ShouldBe("Call back");
        data.GetProperty("priority").GetString().ShouldBe("high");
        data.GetProperty("status").GetString().ShouldBe("open");
    }

    [Fact]
    public async Task Due_today_filter_and_owner_scope()
    {
        var (_, token) = await fixture.RegisterAndLoginAsync(Email(), "Sup3rSecret!");
        var overdueId = await CreateTaskAsync(token, new { title = "Overdue", dueAt = DateTimeOffset.UtcNow.AddDays(-1) });
        await CreateTaskAsync(token, new { title = "Later", dueAt = DateTimeOffset.UtcNow.AddDays(10) });

        // The starter task seeded for this user by ProvisionCrmWorkspace is due tomorrow, so the "due before now" filter
        // returns only the overdue task.
        var cutoff = Uri.EscapeDataString(DateTimeOffset.UtcNow.ToString("o"));
        var due = await fixture.Client.SendAsync(
            fixture.Authed(HttpMethod.Get, $"/v1/crm/tasks?status=open&dueBefore={cutoff}", token));
        (await PlatformApiFactory.ReadData(due)).GetProperty("totalCount").GetInt32().ShouldBe(1);

        // Owner isolation: another user never sees this user's task. Assert the specific task is absent rather than a
        // zero count — every new user is asynchronously seeded their OWN starter task, so "other" is not empty.
        var (_, other) = await fixture.RegisterAndLoginAsync(Email(), "Sup3rSecret!");
        var otherList = await fixture.Client.SendAsync(fixture.Authed(HttpMethod.Get, "/v1/crm/tasks", other));
        var otherIds = (await PlatformApiFactory.ReadData(otherList)).GetProperty("items").EnumerateArray()
            .Select(t => t.GetProperty("id").GetGuid()).ToList();
        otherIds.ShouldNotContain(overdueId);
    }

    [Fact]
    public async Task Complete_is_idempotent()
    {
        var (_, token) = await fixture.RegisterAndLoginAsync(Email(), "Sup3rSecret!");
        var id = await CreateTaskAsync(token, new { title = "Do" });

        var first = await fixture.Client.SendAsync(fixture.Authed(HttpMethod.Post, $"/v1/crm/tasks/{id}/complete", token));
        first.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        var again = await fixture.Client.SendAsync(fixture.Authed(HttpMethod.Post, $"/v1/crm/tasks/{id}/complete", token));
        again.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var get = await fixture.Client.SendAsync(fixture.Authed(HttpMethod.Get, $"/v1/crm/tasks/{id}", token));
        (await PlatformApiFactory.ReadData(get)).GetProperty("status").GetString().ShouldBe("done");
    }

    [Fact]
    public async Task Patch_is_partial()
    {
        var (_, token) = await fixture.RegisterAndLoginAsync(Email(), "Sup3rSecret!");
        var assigneeId = Guid.CreateVersion7();
        var id = await CreateTaskAsync(token, new { title = "Keep", priority = "high", assigneeUserId = assigneeId });

        var patch = await fixture.Client.SendAsync(fixture.Authed(
            HttpMethod.Patch, $"/v1/crm/tasks/{id}", token, new { title = "Renamed" }));
        patch.StatusCode.ShouldBe(HttpStatusCode.OK);
        var data = await PlatformApiFactory.ReadData(patch);
        data.GetProperty("title").GetString().ShouldBe("Renamed");
        data.GetProperty("priority").GetString().ShouldBe("high");
        data.GetProperty("assigneeUserId").GetGuid().ShouldBe(assigneeId);
    }

    [Fact]
    public async Task List_filters_by_assignee()
    {
        var (userId, token) = await fixture.RegisterAndLoginAsync(Email(), "Sup3rSecret!");
        var otherAssignee = Guid.CreateVersion7();
        var mine = await CreateTaskAsync(token, new { title = "Mine", assigneeUserId = userId });
        await CreateTaskAsync(token, new { title = "Other", assigneeUserId = otherAssignee });

        var list = await fixture.Client.SendAsync(fixture.Authed(
            HttpMethod.Get, $"/v1/crm/tasks?assigneeUserId={userId}", token));
        var items = (await PlatformApiFactory.ReadData(list)).GetProperty("items").EnumerateArray().ToList();
        items.Count.ShouldBe(1);
        items[0].GetProperty("id").GetGuid().ShouldBe(mine);
    }

    [Fact]
    public async Task Foreign_task_is_not_found()
    {
        var (_, owner) = await fixture.RegisterAndLoginAsync(Email(), "Sup3rSecret!");
        var id = await CreateTaskAsync(owner, new { title = "Private" });
        var (_, intruder) = await fixture.RegisterAndLoginAsync(Email(), "Sup3rSecret!");
        var get = await fixture.Client.SendAsync(fixture.Authed(HttpMethod.Get, $"/v1/crm/tasks/{id}", intruder));
        get.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }
}
