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

    [Fact]
    public async Task Create_then_get_round_trips()
    {
        var (_, token) = await fixture.RegisterAndLoginAsync(Email(), "Sup3rSecret!");
        var id = await CreateDealAsync(token, new
        {
            title = "Big Deal", amountCents = 250000L, currency = "usd", stage = "qualified", notes = "warm",
        });

        var get = await fixture.Client.SendAsync(fixture.Authed(HttpMethod.Get, $"/v1/crm/deals/{id}", token));
        get.StatusCode.ShouldBe(HttpStatusCode.OK);
        var data = await PlatformApiFactory.ReadData(get);
        data.GetProperty("title").GetString().ShouldBe("Big Deal");
        data.GetProperty("amountCents").GetInt64().ShouldBe(250000);
        data.GetProperty("currency").GetString().ShouldBe("USD");
        data.GetProperty("stage").GetString().ShouldBe("qualified");
    }

    [Fact]
    public async Task List_filters_by_stage_and_is_owner_scoped()
    {
        var (_, token) = await fixture.RegisterAndLoginAsync(Email(), "Sup3rSecret!");
        await CreateDealAsync(token, new { title = "Lead Deal", amountCents = 100L, stage = "lead" });
        await CreateDealAsync(token, new { title = "Won Deal", amountCents = 100L, stage = "won" });

        var won = await fixture.Client.SendAsync(fixture.Authed(HttpMethod.Get, "/v1/crm/deals?stage=won", token));
        (await PlatformApiFactory.ReadData(won)).GetProperty("totalCount").GetInt32().ShouldBe(1);

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
            HttpMethod.Patch, $"/v1/crm/deals/{id}", token, new { amountCents = 999L }));
        patch.StatusCode.ShouldBe(HttpStatusCode.OK);
        var data = await PlatformApiFactory.ReadData(patch);
        data.GetProperty("amountCents").GetInt64().ShouldBe(999);
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
}
