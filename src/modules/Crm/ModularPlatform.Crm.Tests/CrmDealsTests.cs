using System.Net;
using ModularPlatform.IntegrationTesting;
using Shouldly;

namespace ModularPlatform.Crm.Tests;

/// <summary>
/// CRM Deals (revenue pipeline) end-to-end: create→get round-trip, list filtered + owner-scoped, partial patch,
/// stage pipeline with terminal lock + ClosedAt, and soft-delete. Foreign ids are 404s (RLS + explicit WHERE).
/// </summary>
[Collection("Integration")]
public sealed class CrmDealsTests(PlatformApiFactory fixture)
{
    private static string Email() => $"crm-{Guid.CreateVersion7():N}@x.com";

    private async Task<Guid> CreateDealAsync(string token, object body)
    {
        var resp = await fixture.Client.SendAsync(fixture.Authed(HttpMethod.Post, "/v1/crm/deals", token, body));
        resp.StatusCode.ShouldBe(HttpStatusCode.Created, await resp.Content.ReadAsStringAsync());
        return (await PlatformApiFactory.ReadData(resp)).GetProperty("id").GetGuid();
    }

    private async Task<Guid> CreateContactAsync(string token, string firstName = "Deal", string lastName = "Contact")
    {
        var resp = await fixture.Client.SendAsync(fixture.Authed(
            HttpMethod.Post, "/v1/crm/contacts", token, new { firstName, lastName, status = "engaged" }));
        resp.StatusCode.ShouldBe(HttpStatusCode.Created, await resp.Content.ReadAsStringAsync());
        return (await PlatformApiFactory.ReadData(resp)).GetProperty("id").GetGuid();
    }

    private async Task<Guid> CreateCompanyAsync(string token, string name)
    {
        var resp = await fixture.Client.SendAsync(fixture.Authed(HttpMethod.Post, "/v1/crm/companies", token, new { name }));
        resp.StatusCode.ShouldBe(HttpStatusCode.Created, await resp.Content.ReadAsStringAsync());
        return (await PlatformApiFactory.ReadData(resp)).GetProperty("id").GetGuid();
    }

    [Fact]
    public async Task Create_then_get_round_trips()
    {
        var (_, token) = await fixture.RegisterAndLoginAsync(Email(), "Sup3rSecret!");
        var id = await CreateDealAsync(token, new
        {
            title = "Big Deal",
            amountCents = 250000L,
            currency = "usd",
            stage = "qualified",
            probabilityPercent = 40,
            leadSource = "referral",
            nextStep = "Send proposal",
            notes = "warm",
        });

        var get = await fixture.Client.SendAsync(fixture.Authed(HttpMethod.Get, $"/v1/crm/deals/{id}", token));
        get.StatusCode.ShouldBe(HttpStatusCode.OK);
        var data = await PlatformApiFactory.ReadData(get);
        data.GetProperty("title").GetString().ShouldBe("Big Deal");
        data.GetProperty("amountCents").GetInt64().ShouldBe(250000);
        data.GetProperty("currency").GetString().ShouldBe("USD");
        data.GetProperty("stage").GetString().ShouldBe("qualified");
        data.GetProperty("probabilityPercent").GetInt32().ShouldBe(40);
        data.GetProperty("leadSource").GetString().ShouldBe("referral");
        data.GetProperty("nextStep").GetString().ShouldBe("Send proposal");
    }

    [Fact]
    public async Task Create_links_company_explicitly_or_from_contact()
    {
        var (_, token) = await fixture.RegisterAndLoginAsync(Email(), "Sup3rSecret!");
        var companyId = await CreateCompanyAsync(token, "Acme");
        var contact = await fixture.Client.SendAsync(fixture.Authed(
            HttpMethod.Post, "/v1/crm/contacts", token,
            new { firstName = "Jane", lastName = "Buyer", status = "engaged", companyId }));
        contact.StatusCode.ShouldBe(HttpStatusCode.Created, await contact.Content.ReadAsStringAsync());
        var contactId = (await PlatformApiFactory.ReadData(contact)).GetProperty("id").GetGuid();

        var fromCompany = await CreateDealAsync(token, new { companyId, title = "Company Deal", amountCents = 100L, stage = "lead" });
        var fromContact = await CreateDealAsync(token, new { contactId, title = "Contact Deal", amountCents = 100L, stage = "lead" });

        var explicitGet = await fixture.Client.SendAsync(fixture.Authed(HttpMethod.Get, $"/v1/crm/deals/{fromCompany}", token));
        (await PlatformApiFactory.ReadData(explicitGet)).GetProperty("companyId").GetGuid().ShouldBe(companyId);

        var derivedGet = await fixture.Client.SendAsync(fixture.Authed(HttpMethod.Get, $"/v1/crm/deals/{fromContact}", token));
        (await PlatformApiFactory.ReadData(derivedGet)).GetProperty("companyId").GetGuid().ShouldBe(companyId);
    }

    [Fact]
    public async Task List_filters_by_stage_and_is_owner_scoped()
    {
        var (_, token) = await fixture.RegisterAndLoginAsync(Email(), "Sup3rSecret!");
        await CreateDealAsync(token, new { title = "Lead Deal", amountCents = 100L, stage = "lead", leadSource = "web" });
        await CreateDealAsync(token, new { title = "Won Deal", amountCents = 100L, stage = "won", leadSource = "referral" });

        var won = await fixture.Client.SendAsync(fixture.Authed(HttpMethod.Get, "/v1/crm/deals?stage=won", token));
        (await PlatformApiFactory.ReadData(won)).GetProperty("totalCount").GetInt32().ShouldBe(1);

        var web = await fixture.Client.SendAsync(fixture.Authed(HttpMethod.Get, "/v1/crm/deals?leadSource=web", token));
        (await PlatformApiFactory.ReadData(web)).GetProperty("totalCount").GetInt32().ShouldBe(1);

        var (_, other) = await fixture.RegisterAndLoginAsync(Email(), "Sup3rSecret!");
        var otherList = await fixture.Client.SendAsync(fixture.Authed(HttpMethod.Get, "/v1/crm/deals", other));
        (await PlatformApiFactory.ReadData(otherList)).GetProperty("totalCount").GetInt32().ShouldBe(0);
    }

    [Fact]
    public async Task Stage_pipeline_advances_then_locks_when_terminal()
    {
        var (_, token) = await fixture.RegisterAndLoginAsync(Email(), "Sup3rSecret!");
        var id = await CreateDealAsync(token, new { title = "Pipe", amountCents = 5000L, stage = "lead" });

        var move = await fixture.Client.SendAsync(fixture.Authed(
            HttpMethod.Post, $"/v1/crm/deals/{id}/stage", token, new { stage = "won" }));
        move.StatusCode.ShouldBe(HttpStatusCode.OK);
        var data = await PlatformApiFactory.ReadData(move);
        data.GetProperty("stage").GetString().ShouldBe("won");
        data.GetProperty("lastStage").GetString().ShouldBe("lead");
        data.GetProperty("probabilityPercent").GetInt32().ShouldBe(100);
        data.GetProperty("closedAt").ValueKind.ShouldNotBe(System.Text.Json.JsonValueKind.Null);

        // A closed deal cannot move again.
        var reopen = await fixture.Client.SendAsync(fixture.Authed(
            HttpMethod.Post, $"/v1/crm/deals/{id}/stage", token, new { stage = "negotiation" }));
        reopen.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task Patch_is_partial_and_keeps_stage()
    {
        var (_, token) = await fixture.RegisterAndLoginAsync(Email(), "Sup3rSecret!");
        var id = await CreateDealAsync(token, new { title = "Keep", amountCents = 100L, stage = "proposal" });

        var patch = await fixture.Client.SendAsync(fixture.Authed(
            HttpMethod.Patch, $"/v1/crm/deals/{id}", token,
            new { amountCents = 999L, probabilityPercent = 60, leadSource = "web", nextStep = "Book demo" }));
        patch.StatusCode.ShouldBe(HttpStatusCode.OK);
        var data = await PlatformApiFactory.ReadData(patch);
        data.GetProperty("amountCents").GetInt64().ShouldBe(999);
        data.GetProperty("probabilityPercent").GetInt32().ShouldBe(60);
        data.GetProperty("leadSource").GetString().ShouldBe("web");
        data.GetProperty("nextStep").GetString().ShouldBe("Book demo");
        data.GetProperty("stage").GetString().ShouldBe("proposal");
        data.GetProperty("title").GetString().ShouldBe("Keep");
    }

    [Fact]
    public async Task Foreign_deal_is_not_found()
    {
        var (_, owner) = await fixture.RegisterAndLoginAsync(Email(), "Sup3rSecret!");
        var id = await CreateDealAsync(owner, new { title = "Private", amountCents = 1L, stage = "lead" });

        var (_, intruder) = await fixture.RegisterAndLoginAsync(Email(), "Sup3rSecret!");
        var get = await fixture.Client.SendAsync(fixture.Authed(HttpMethod.Get, $"/v1/crm/deals/{id}", intruder));
        get.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Delete_soft_deletes_and_hides()
    {
        var (_, token) = await fixture.RegisterAndLoginAsync(Email(), "Sup3rSecret!");
        var id = await CreateDealAsync(token, new { title = "Bin", amountCents = 1L, stage = "lead" });

        var del = await fixture.Client.SendAsync(fixture.Authed(HttpMethod.Delete, $"/v1/crm/deals/{id}", token));
        del.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var get = await fixture.Client.SendAsync(fixture.Authed(HttpMethod.Get, $"/v1/crm/deals/{id}", token));
        get.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Linking_a_foreign_contact_is_not_found()
    {
        var (_, owner) = await fixture.RegisterAndLoginAsync(Email(), "Sup3rSecret!");
        var (_, other) = await fixture.RegisterAndLoginAsync(Email(), "Sup3rSecret!");
        var resp = await fixture.Client.SendAsync(fixture.Authed(HttpMethod.Post, "/v1/crm/deals", owner,
            new { title = "X", amountCents = 1L, stage = "lead", contactId = Guid.CreateVersion7() }));
        resp.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Move_deal_stage_same_stage_no_op_then_normal_advance()
    {
        var (_, token) = await fixture.RegisterAndLoginAsync(Email(), "Sup3rSecret!");
        var id = await CreateDealAsync(token, new { title = "Steady", amountCents = 1000L, stage = "lead" });

        var sameStage = await fixture.Client.SendAsync(fixture.Authed(
            HttpMethod.Post, $"/v1/crm/deals/{id}/stage", token, new { stage = "lead" }));
        sameStage.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await PlatformApiFactory.ReadData(sameStage)).GetProperty("stage").GetString().ShouldBe("lead");

        var advance = await fixture.Client.SendAsync(fixture.Authed(
            HttpMethod.Post, $"/v1/crm/deals/{id}/stage", token, new { stage = "qualified" }));
        advance.StatusCode.ShouldBe(HttpStatusCode.OK);
        var afterData = await PlatformApiFactory.ReadData(advance);
        afterData.GetProperty("stage").GetString().ShouldBe("qualified");
    }

    [Fact]
    public async Task Meetings_can_be_linked_and_listed_by_deal()
    {
        var (_, token) = await fixture.RegisterAndLoginAsync(Email(), "Sup3rSecret!");
        var contactId = await CreateContactAsync(token, "Jane", "Deal");
        var dealId = await CreateDealAsync(token, new
        {
            contactId, title = "Deal Hub", amountCents = 1000L, stage = "lead",
        });

        var meeting = await fixture.Client.SendAsync(fixture.Authed(HttpMethod.Post, "/v1/crm/meetings", token,
            new
            {
                contactId,
                dealId,
                title = "Negotiation call",
                scheduledAt = DateTimeOffset.UtcNow.AddDays(1),
                durationMinutes = 30,
            }));
        meeting.StatusCode.ShouldBe(HttpStatusCode.Created, await meeting.Content.ReadAsStringAsync());
        var meetingId = (await PlatformApiFactory.ReadData(meeting)).GetProperty("id").GetGuid();

        var byDeal = await fixture.Client.SendAsync(
            fixture.Authed(HttpMethod.Get, $"/v1/crm/meetings?dealId={dealId}", token));
        byDeal.StatusCode.ShouldBe(HttpStatusCode.OK);
        var data = await PlatformApiFactory.ReadData(byDeal);
        data.GetProperty("totalCount").GetInt32().ShouldBe(1);
        data.GetProperty("items")[0].GetProperty("id").GetGuid().ShouldBe(meetingId);
        data.GetProperty("items")[0].GetProperty("dealId").GetGuid().ShouldBe(dealId);
        data.GetProperty("items")[0].GetProperty("contactName").GetString().ShouldBe("Jane Deal");
    }
}
