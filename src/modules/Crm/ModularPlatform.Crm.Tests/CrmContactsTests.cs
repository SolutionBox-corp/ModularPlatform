using System.Net;
using System.Net.Http.Json;
using ModularPlatform.IntegrationTesting;
using Shouldly;

namespace ModularPlatform.Crm.Tests;

/// <summary>
/// CRM Contacts end-to-end over the real Api host: create→get round-trips (incl. the encrypted name/e-mail via the
/// read converter), list is filtered + owner-scoped, update mutates, delete soft-deletes, a foreign contact is a 404
/// (RLS), interactions add+list, and invalid input is a 400. The test tenant is auto-entitled to "crm"
/// (ProductModuleKeys.DefaultEntitled), so RequireModule("crm") passes.
/// </summary>
[Collection("Integration")]
public sealed class CrmContactsTests(PlatformApiFactory fixture)
{
    private static string Email() => $"crm-{Guid.CreateVersion7():N}@x.com";

    private async Task<Guid> CreateContactAsync(string token, object body)
    {
        var resp = await fixture.Client.SendAsync(fixture.Authed(HttpMethod.Post, "/v1/crm/contacts", token, body));
        resp.StatusCode.ShouldBe(HttpStatusCode.Created, await resp.Content.ReadAsStringAsync());
        return (await PlatformApiFactory.ReadData(resp)).GetProperty("id").GetGuid();
    }

    [Fact]
    public async Task Create_then_get_round_trips_including_encrypted_fields()
    {
        var (_, token) = await fixture.RegisterAndLoginAsync(Email(), "Sup3rSecret!");

        var id = await CreateContactAsync(token, new
        {
            fullName = "Jane Doe",
            email = "jane@acme.test",
            phone = "+420123456789",
            company = "Acme",
            position = "CTO",
            notes = "met at conf",
            tags = new[] { "vip", "warm" },
            status = "active",
        });

        var get = await fixture.Client.SendAsync(fixture.Authed(HttpMethod.Get, $"/v1/crm/contacts/{id}", token));
        get.StatusCode.ShouldBe(HttpStatusCode.OK);
        var data = await PlatformApiFactory.ReadData(get);
        data.GetProperty("fullName").GetString().ShouldBe("Jane Doe");       // decrypted at rest
        data.GetProperty("email").GetString().ShouldBe("jane@acme.test");    // decrypted at rest
        data.GetProperty("phone").GetString().ShouldBe("+420123456789");
        data.GetProperty("company").GetString().ShouldBe("Acme");
        data.GetProperty("status").GetString().ShouldBe("active");
        data.GetProperty("tags").EnumerateArray().Select(t => t.GetString()).ShouldBe(["vip", "warm"]);
    }

    [Fact]
    public async Task List_filters_by_status_and_email_and_is_owner_scoped()
    {
        var (_, token) = await fixture.RegisterAndLoginAsync(Email(), "Sup3rSecret!");
        await CreateContactAsync(token, new { fullName = "Lead One", status = "lead" });
        var targetEmail = $"target-{Guid.CreateVersion7():N}@acme.test";
        await CreateContactAsync(token, new { fullName = "Customer One", email = targetEmail, status = "customer" });

        // Filter by status.
        var byStatus = await fixture.Client.SendAsync(
            fixture.Authed(HttpMethod.Get, "/v1/crm/contacts?status=customer", token));
        var statusData = await PlatformApiFactory.ReadData(byStatus);
        statusData.GetProperty("total").GetInt32().ShouldBe(1);
        statusData.GetProperty("items")[0].GetProperty("fullName").GetString().ShouldBe("Customer One");

        // Filter by exact e-mail (blind index).
        var byEmail = await fixture.Client.SendAsync(
            fixture.Authed(HttpMethod.Get, $"/v1/crm/contacts?email={Uri.EscapeDataString(targetEmail)}", token));
        (await PlatformApiFactory.ReadData(byEmail)).GetProperty("total").GetInt32().ShouldBe(1);

        // A different user sees none of the above contacts.
        var (_, otherToken) = await fixture.RegisterAndLoginAsync(Email(), "Sup3rSecret!");
        var otherList = await fixture.Client.SendAsync(fixture.Authed(HttpMethod.Get, "/v1/crm/contacts", otherToken));
        (await PlatformApiFactory.ReadData(otherList)).GetProperty("total").GetInt32().ShouldBe(0);
    }

    [Fact]
    public async Task Update_changes_fields()
    {
        var (_, token) = await fixture.RegisterAndLoginAsync(Email(), "Sup3rSecret!");
        var id = await CreateContactAsync(token, new { fullName = "Old Name", status = "lead" });

        var update = await fixture.Client.SendAsync(fixture.Authed(HttpMethod.Patch, $"/v1/crm/contacts/{id}", token,
            new { fullName = "New Name", company = "NewCo", status = "active" }));
        update.StatusCode.ShouldBe(HttpStatusCode.OK);
        var data = await PlatformApiFactory.ReadData(update);
        data.GetProperty("fullName").GetString().ShouldBe("New Name");
        data.GetProperty("company").GetString().ShouldBe("NewCo");
        data.GetProperty("status").GetString().ShouldBe("active");
    }

    [Fact]
    public async Task Delete_soft_deletes_and_hides_from_get_and_list()
    {
        var (_, token) = await fixture.RegisterAndLoginAsync(Email(), "Sup3rSecret!");
        var id = await CreateContactAsync(token, new { fullName = "To Delete", status = "lead" });

        var del = await fixture.Client.SendAsync(fixture.Authed(HttpMethod.Delete, $"/v1/crm/contacts/{id}", token));
        del.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var get = await fixture.Client.SendAsync(fixture.Authed(HttpMethod.Get, $"/v1/crm/contacts/{id}", token));
        get.StatusCode.ShouldBe(HttpStatusCode.NotFound);

        var list = await fixture.Client.SendAsync(fixture.Authed(HttpMethod.Get, "/v1/crm/contacts", token));
        var items = (await PlatformApiFactory.ReadData(list)).GetProperty("items").EnumerateArray()
            .Select(i => i.GetProperty("id").GetGuid());
        items.ShouldNotContain(id);
    }

    [Fact]
    public async Task Foreign_contact_is_not_found()
    {
        var (_, ownerToken) = await fixture.RegisterAndLoginAsync(Email(), "Sup3rSecret!");
        var id = await CreateContactAsync(ownerToken, new { fullName = "Private", status = "lead" });

        var (_, intruderToken) = await fixture.RegisterAndLoginAsync(Email(), "Sup3rSecret!");
        var get = await fixture.Client.SendAsync(
            fixture.Authed(HttpMethod.Get, $"/v1/crm/contacts/{id}", intruderToken));
        get.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Interactions_add_and_list_newest_first()
    {
        var (_, token) = await fixture.RegisterAndLoginAsync(Email(), "Sup3rSecret!");
        var id = await CreateContactAsync(token, new { fullName = "Talkative", status = "active" });

        var first = await fixture.Client.SendAsync(fixture.Authed(
            HttpMethod.Post, $"/v1/crm/contacts/{id}/interactions", token,
            new { type = "call", occurredAt = DateTimeOffset.UtcNow.AddMinutes(-10), body = "called" }));
        first.StatusCode.ShouldBe(HttpStatusCode.Created);

        var second = await fixture.Client.SendAsync(fixture.Authed(
            HttpMethod.Post, $"/v1/crm/contacts/{id}/interactions", token,
            new { type = "note", body = "follow up" }));
        second.StatusCode.ShouldBe(HttpStatusCode.Created);

        var list = await fixture.Client.SendAsync(
            fixture.Authed(HttpMethod.Get, $"/v1/crm/contacts/{id}/interactions", token));
        var items = (await PlatformApiFactory.ReadData(list)).EnumerateArray().ToList();
        items.Count.ShouldBe(2);
        items[0].GetProperty("type").GetString().ShouldBe("note"); // newest first
    }

    [Fact]
    public async Task Adding_interaction_to_foreign_contact_is_not_found()
    {
        var (_, ownerToken) = await fixture.RegisterAndLoginAsync(Email(), "Sup3rSecret!");
        var id = await CreateContactAsync(ownerToken, new { fullName = "Owned", status = "lead" });

        var (_, intruderToken) = await fixture.RegisterAndLoginAsync(Email(), "Sup3rSecret!");
        var resp = await fixture.Client.SendAsync(fixture.Authed(
            HttpMethod.Post, $"/v1/crm/contacts/{id}/interactions", intruderToken, new { type = "call" }));
        resp.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Invalid_status_is_rejected()
    {
        var (_, token) = await fixture.RegisterAndLoginAsync(Email(), "Sup3rSecret!");
        var resp = await fixture.Client.SendAsync(fixture.Authed(
            HttpMethod.Post, "/v1/crm/contacts", token, new { fullName = "Bad", status = "bogus" }));
        resp.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }
}
