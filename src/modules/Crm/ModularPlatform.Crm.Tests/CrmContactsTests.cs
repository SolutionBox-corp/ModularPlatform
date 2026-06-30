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
            firstName = "Jane",
            lastName = "Doe",
            email = "jane@acme.test",
            phone = "+420123456789",
            position = "CTO",
            notes = "met at conf",
            tags = new[] { "vip", "warm" },
            status = "active",
        });

        var get = await fixture.Client.SendAsync(fixture.Authed(HttpMethod.Get, $"/v1/crm/contacts/{id}", token));
        get.StatusCode.ShouldBe(HttpStatusCode.OK);
        var data = await PlatformApiFactory.ReadData(get);
        data.GetProperty("firstName").GetString().ShouldBe("Jane");          // decrypted at rest
        data.GetProperty("lastName").GetString().ShouldBe("Doe");            // decrypted at rest
        data.GetProperty("email").GetString().ShouldBe("jane@acme.test");    // decrypted at rest
        data.GetProperty("phone").GetString().ShouldBe("+420123456789");
        data.GetProperty("status").GetString().ShouldBe("active");
        data.GetProperty("tags").EnumerateArray().Select(t => t.GetString()).ShouldBe(["vip", "warm"]);
    }

    [Fact]
    public async Task List_filters_by_status_and_email_and_is_owner_scoped()
    {
        var (_, token) = await fixture.RegisterAndLoginAsync(Email(), "Sup3rSecret!");
        await CreateContactAsync(token, new { firstName = "Lead", lastName = "One", status = "lead" });
        var targetEmail = $"target-{Guid.CreateVersion7():N}@acme.test";
        await CreateContactAsync(token, new { firstName = "Customer", lastName = "One", email = targetEmail, status = "customer" });

        // Filter by status.
        var byStatus = await fixture.Client.SendAsync(
            fixture.Authed(HttpMethod.Get, "/v1/crm/contacts?status=customer", token));
        var statusData = await PlatformApiFactory.ReadData(byStatus);
        statusData.GetProperty("totalCount").GetInt32().ShouldBe(1);
        statusData.GetProperty("items")[0].GetProperty("firstName").GetString().ShouldBe("Customer");

        // Filter by exact e-mail (blind index).
        var byEmail = await fixture.Client.SendAsync(
            fixture.Authed(HttpMethod.Get, $"/v1/crm/contacts?email={Uri.EscapeDataString(targetEmail)}", token));
        (await PlatformApiFactory.ReadData(byEmail)).GetProperty("totalCount").GetInt32().ShouldBe(1);

        // A different user sees none of the above contacts.
        var (_, otherToken) = await fixture.RegisterAndLoginAsync(Email(), "Sup3rSecret!");
        var otherList = await fixture.Client.SendAsync(fixture.Authed(HttpMethod.Get, "/v1/crm/contacts", otherToken));
        (await PlatformApiFactory.ReadData(otherList)).GetProperty("totalCount").GetInt32().ShouldBe(0);
    }

    [Fact]
    public async Task Update_changes_fields()
    {
        var (_, token) = await fixture.RegisterAndLoginAsync(Email(), "Sup3rSecret!");
        var id = await CreateContactAsync(token, new { firstName = "Old", lastName = "Name", status = "lead" });

        var update = await fixture.Client.SendAsync(fixture.Authed(HttpMethod.Patch, $"/v1/crm/contacts/{id}", token,
            new { firstName = "New", lastName = "Name", status = "active" }));
        update.StatusCode.ShouldBe(HttpStatusCode.OK);
        var data = await PlatformApiFactory.ReadData(update);
        data.GetProperty("firstName").GetString().ShouldBe("New");
        data.GetProperty("lastName").GetString().ShouldBe("Name");
        data.GetProperty("status").GetString().ShouldBe("active");
    }

    [Fact]
    public async Task Delete_soft_deletes_and_hides_from_get_and_list()
    {
        var (_, token) = await fixture.RegisterAndLoginAsync(Email(), "Sup3rSecret!");
        var id = await CreateContactAsync(token, new { firstName = "To", lastName = "Delete", status = "lead" });

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
        var id = await CreateContactAsync(ownerToken, new { firstName = "Private", lastName = "Contact", status = "lead" });

        var (_, intruderToken) = await fixture.RegisterAndLoginAsync(Email(), "Sup3rSecret!");
        var get = await fixture.Client.SendAsync(
            fixture.Authed(HttpMethod.Get, $"/v1/crm/contacts/{id}", intruderToken));
        get.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Interactions_add_and_list_newest_first()
    {
        var (_, token) = await fixture.RegisterAndLoginAsync(Email(), "Sup3rSecret!");
        var id = await CreateContactAsync(token, new { firstName = "Talkative", lastName = "Contact", status = "active" });

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
        var items = (await PlatformApiFactory.ReadData(list)).GetProperty("items").EnumerateArray().ToList();
        items.Count.ShouldBe(2);
        items[0].GetProperty("type").GetString().ShouldBe("note"); // newest first
    }

    [Fact]
    public async Task Patch_is_partial_and_does_not_reset_status_or_wipe_fields()
    {
        var (_, token) = await fixture.RegisterAndLoginAsync(Email(), "Sup3rSecret!");
        var id = await CreateContactAsync(token, new
        {
            firstName = "Keep", lastName = "Me", status = "customer", tags = new[] { "vip" },
        });

        // Send ONLY a notes change — status/name/tags must survive (no full-replace, no silent reset to lead).
        var patch = await fixture.Client.SendAsync(fixture.Authed(HttpMethod.Patch, $"/v1/crm/contacts/{id}", token,
            new { notes = "called twice" }));
        patch.StatusCode.ShouldBe(HttpStatusCode.OK);
        var data = await PlatformApiFactory.ReadData(patch);
        data.GetProperty("status").GetString().ShouldBe("customer");
        data.GetProperty("firstName").GetString().ShouldBe("Keep");
        data.GetProperty("lastName").GetString().ShouldBe("Me");
        data.GetProperty("notes").GetString().ShouldBe("called twice");
        data.GetProperty("tags").EnumerateArray().Select(t => t.GetString()).ShouldBe(["vip"]);
    }

    [Fact]
    public async Task Adding_interaction_to_foreign_contact_is_not_found()
    {
        var (_, ownerToken) = await fixture.RegisterAndLoginAsync(Email(), "Sup3rSecret!");
        var id = await CreateContactAsync(ownerToken, new { firstName = "Owned", lastName = "Contact", status = "lead" });

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
            HttpMethod.Post, "/v1/crm/contacts", token, new { firstName = "Bad", lastName = "Contact", status = "bogus" }));
        resp.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Patch_detaches_company_with_explicit_null_and_leaves_it_when_omitted()
    {
        var (_, token) = await fixture.RegisterAndLoginAsync(Email(), "Sup3rSecret!");

        var companyResp = await fixture.Client.SendAsync(fixture.Authed(
            HttpMethod.Post, "/v1/crm/companies", token, new { name = "Acme" }));
        companyResp.StatusCode.ShouldBe(HttpStatusCode.Created);
        var companyId = (await PlatformApiFactory.ReadData(companyResp)).GetProperty("id").GetGuid();

        var contactId = await CreateContactAsync(token, new { firstName = "Joe", lastName = "Contact", status = "lead", companyId });

        var omitPatch = await fixture.Client.SendAsync(fixture.Authed(
            HttpMethod.Patch, $"/v1/crm/contacts/{contactId}", token, new { status = "active" }));
        omitPatch.StatusCode.ShouldBe(HttpStatusCode.OK);
        var afterOmit = await fixture.Client.SendAsync(fixture.Authed(HttpMethod.Get, $"/v1/crm/contacts/{contactId}", token));
        var omitData = await PlatformApiFactory.ReadData(afterOmit);
        omitData.GetProperty("companyId").GetGuid().ShouldBe(companyId);

        var nullPatch = await fixture.Client.SendAsync(fixture.Authed(
            HttpMethod.Patch, $"/v1/crm/contacts/{contactId}", token, new { companyId = (Guid?)null }));
        nullPatch.StatusCode.ShouldBe(HttpStatusCode.OK);
        var afterNull = await fixture.Client.SendAsync(fixture.Authed(HttpMethod.Get, $"/v1/crm/contacts/{contactId}", token));
        var nullData = await PlatformApiFactory.ReadData(afterNull);
        nullData.GetProperty("companyId").ValueKind.ShouldBe(System.Text.Json.JsonValueKind.Null);
    }
}
