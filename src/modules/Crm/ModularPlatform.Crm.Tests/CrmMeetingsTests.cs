using System.Net;
using System.Net.Http.Json;
using ModularPlatform.IntegrationTesting;
using Shouldly;

namespace ModularPlatform.Crm.Tests;

/// <summary>
/// CRM Meetings end-to-end: create→get, list ordered + filtered + owner-scoped, reschedule, cancel, and complete
/// (which also drops a "meeting" interaction onto the linked contact's timeline). Foreign ids are 404s (RLS).
/// </summary>
[Collection("Integration")]
public sealed class CrmMeetingsTests(PlatformApiFactory fixture)
{
    private static string Email() => $"crm-{Guid.CreateVersion7():N}@x.com";

    private async Task<Guid> CreateContactAsync(string token, string name)
    {
        var resp = await fixture.Client.SendAsync(fixture.Authed(
            HttpMethod.Post, "/v1/crm/contacts", token, new { firstName = name, lastName = "Contact", status = "engaged" }));
        resp.StatusCode.ShouldBe(HttpStatusCode.Created, await resp.Content.ReadAsStringAsync());
        return (await PlatformApiFactory.ReadData(resp)).GetProperty("id").GetGuid();
    }

    private async Task<Guid> CreateMeetingAsync(string token, object body)
    {
        var resp = await fixture.Client.SendAsync(fixture.Authed(HttpMethod.Post, "/v1/crm/meetings", token, body));
        resp.StatusCode.ShouldBe(HttpStatusCode.Created, await resp.Content.ReadAsStringAsync());
        return (await PlatformApiFactory.ReadData(resp)).GetProperty("id").GetGuid();
    }

    [Fact]
    public async Task Create_then_get_round_trips()
    {
        var (_, token) = await fixture.RegisterAndLoginAsync(Email(), "Sup3rSecret!");
        var contactId = await CreateContactAsync(token, "Jane");
        var when = DateTimeOffset.UtcNow.AddDays(2);

        var id = await CreateMeetingAsync(token, new
        {
            contactId,
            title = "Intro call",
            scheduledAt = when,
            durationMinutes = 30,
            location = "Google Meet",
            notes = "discovery",
        });

        var get = await fixture.Client.SendAsync(fixture.Authed(HttpMethod.Get, $"/v1/crm/meetings/{id}", token));
        get.StatusCode.ShouldBe(HttpStatusCode.OK);
        var data = await PlatformApiFactory.ReadData(get);
        data.GetProperty("title").GetString().ShouldBe("Intro call");
        data.GetProperty("durationMinutes").GetInt32().ShouldBe(30);
        data.GetProperty("status").GetString().ShouldBe("planned");
        data.GetProperty("contactId").GetGuid().ShouldBe(contactId);
    }

    [Fact]
    public async Task Create_with_foreign_contact_is_not_found()
    {
        var (_, ownerToken) = await fixture.RegisterAndLoginAsync(Email(), "Sup3rSecret!");
        var contactId = await CreateContactAsync(ownerToken, "Owned");

        var (_, intruderToken) = await fixture.RegisterAndLoginAsync(Email(), "Sup3rSecret!");
        var resp = await fixture.Client.SendAsync(fixture.Authed(HttpMethod.Post, "/v1/crm/meetings", intruderToken,
            new { contactId, title = "Hijack", scheduledAt = DateTimeOffset.UtcNow.AddDays(1), durationMinutes = 15 }));
        resp.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task List_is_ordered_soonest_first_and_filtered_by_status()
    {
        var (_, token) = await fixture.RegisterAndLoginAsync(Email(), "Sup3rSecret!");
        var contactId = await CreateContactAsync(token, "Meeting");
        var later = await CreateMeetingAsync(token, new
        {
            contactId, title = "Later", scheduledAt = DateTimeOffset.UtcNow.AddDays(5), durationMinutes = 60,
        });
        var sooner = await CreateMeetingAsync(token, new
        {
            contactId, title = "Sooner", scheduledAt = DateTimeOffset.UtcNow.AddDays(1), durationMinutes = 60,
        });

        var list = await fixture.Client.SendAsync(fixture.Authed(HttpMethod.Get, "/v1/crm/meetings", token));
        var items = (await PlatformApiFactory.ReadData(list)).GetProperty("items").EnumerateArray().ToList();
        items.Count.ShouldBe(2);
        items[0].GetProperty("id").GetGuid().ShouldBe(sooner); // soonest first
        items[1].GetProperty("id").GetGuid().ShouldBe(later);

        // Cancel one, then filter by status=planned → only the other remains.
        await fixture.Client.SendAsync(fixture.Authed(HttpMethod.Post, $"/v1/crm/meetings/{later}/cancel", token));
        var planned = await fixture.Client.SendAsync(
            fixture.Authed(HttpMethod.Get, "/v1/crm/meetings?status=planned", token));
        (await PlatformApiFactory.ReadData(planned)).GetProperty("totalCount").GetInt32().ShouldBe(1);
    }

    [Fact]
    public async Task Update_reschedules_the_meeting()
    {
        var (_, token) = await fixture.RegisterAndLoginAsync(Email(), "Sup3rSecret!");
        var contactId = await CreateContactAsync(token, "Meeting");
        var id = await CreateMeetingAsync(token, new
        {
            contactId, title = "Old", scheduledAt = DateTimeOffset.UtcNow.AddDays(1), durationMinutes = 30,
        });

        var update = await fixture.Client.SendAsync(fixture.Authed(HttpMethod.Patch, $"/v1/crm/meetings/{id}", token,
            new { title = "New", scheduledAt = DateTimeOffset.UtcNow.AddDays(3), durationMinutes = 45 }));
        update.StatusCode.ShouldBe(HttpStatusCode.OK);
        var data = await PlatformApiFactory.ReadData(update);
        data.GetProperty("title").GetString().ShouldBe("New");
        data.GetProperty("durationMinutes").GetInt32().ShouldBe(45);
    }

    [Fact]
    public async Task Complete_marks_done_and_logs_interaction_on_contact()
    {
        var (_, token) = await fixture.RegisterAndLoginAsync(Email(), "Sup3rSecret!");
        var contactId = await CreateContactAsync(token, "Lead");
        var id = await CreateMeetingAsync(token, new
        {
            contactId, title = "Demo", scheduledAt = DateTimeOffset.UtcNow.AddDays(1), durationMinutes = 30,
        });

        var complete = await fixture.Client.SendAsync(fixture.Authed(
            HttpMethod.Post, $"/v1/crm/meetings/{id}/complete", token, new { outcome = "went well, follow up" }));
        complete.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var get = await fixture.Client.SendAsync(fixture.Authed(HttpMethod.Get, $"/v1/crm/meetings/{id}", token));
        var meeting = await PlatformApiFactory.ReadData(get);
        meeting.GetProperty("status").GetString().ShouldBe("done");
        meeting.GetProperty("outcome").GetString().ShouldBe("went well, follow up");

        // The completion shows up on the contact's interaction timeline.
        var timeline = await fixture.Client.SendAsync(
            fixture.Authed(HttpMethod.Get, $"/v1/crm/contacts/{contactId}/interactions", token));
        var interactions = (await PlatformApiFactory.ReadData(timeline)).GetProperty("items").EnumerateArray().ToList();
        interactions.ShouldContain(i => i.GetProperty("type").GetString() == "meeting");

        // Idempotent: a second complete (double-click / retry) must NOT add a second timeline row.
        var again = await fixture.Client.SendAsync(fixture.Authed(
            HttpMethod.Post, $"/v1/crm/meetings/{id}/complete", token, new { outcome = "dup" }));
        again.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        var timeline2 = await fixture.Client.SendAsync(
            fixture.Authed(HttpMethod.Get, $"/v1/crm/contacts/{contactId}/interactions", token));
        var count = (await PlatformApiFactory.ReadData(timeline2)).GetProperty("items").EnumerateArray()
            .Count(i => i.GetProperty("type").GetString() == "meeting");
        count.ShouldBe(1);
    }

    [Fact]
    public async Task Cancelling_a_completed_meeting_is_rejected()
    {
        var (_, token) = await fixture.RegisterAndLoginAsync(Email(), "Sup3rSecret!");
        var contactId = await CreateContactAsync(token, "Meeting");
        var id = await CreateMeetingAsync(token, new
        {
            contactId, title = "Done", scheduledAt = DateTimeOffset.UtcNow.AddDays(1), durationMinutes = 30,
        });
        await fixture.Client.SendAsync(fixture.Authed(HttpMethod.Post, $"/v1/crm/meetings/{id}/complete", token, new { outcome = (string?)null }));

        var cancel = await fixture.Client.SendAsync(fixture.Authed(HttpMethod.Post, $"/v1/crm/meetings/{id}/cancel", token));
        cancel.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task Foreign_meeting_is_not_found()
    {
        var (_, ownerToken) = await fixture.RegisterAndLoginAsync(Email(), "Sup3rSecret!");
        var contactId = await CreateContactAsync(ownerToken, "Meeting");
        var id = await CreateMeetingAsync(ownerToken, new
        {
            contactId, title = "Private", scheduledAt = DateTimeOffset.UtcNow.AddDays(1), durationMinutes = 30,
        });

        var (_, intruderToken) = await fixture.RegisterAndLoginAsync(Email(), "Sup3rSecret!");
        var get = await fixture.Client.SendAsync(fixture.Authed(HttpMethod.Get, $"/v1/crm/meetings/{id}", intruderToken));
        get.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Invalid_duration_is_rejected()
    {
        var (_, token) = await fixture.RegisterAndLoginAsync(Email(), "Sup3rSecret!");
        var contactId = await CreateContactAsync(token, "Meeting");
        var resp = await fixture.Client.SendAsync(fixture.Authed(HttpMethod.Post, "/v1/crm/meetings", token,
            new { contactId, title = "Bad", scheduledAt = DateTimeOffset.UtcNow.AddDays(1), durationMinutes = 0 }));
        resp.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }
}
