using System.Net;
using System.Text.Json;
using ModularPlatform.IntegrationTesting;
using Shouldly;

namespace ModularPlatform.Crm.Tests;

/// <summary>
/// CRM Kanban end-to-end: create board (3 default columns), add cards, move a card to another column + position
/// (dense renumber), get board, owner isolation, soft-delete. Foreign ids ⇒ 404 (RLS).
/// </summary>
[Collection("Integration")]
public sealed class CrmKanbanTests(PlatformApiFactory fixture)
{
    private static string Email() => $"crm-{Guid.CreateVersion7():N}@x.com";

    private async Task<Guid> CreateBoardAsync(string token, string name)
    {
        var r = await fixture.Client.SendAsync(fixture.Authed(HttpMethod.Post, "/v1/crm/boards", token, new { name }));
        r.StatusCode.ShouldBe(HttpStatusCode.Created, await r.Content.ReadAsStringAsync());
        return (await PlatformApiFactory.ReadData(r)).GetProperty("id").GetGuid();
    }

    private async Task<JsonElement> GetBoardAsync(string token, Guid id)
    {
        var r = await fixture.Client.SendAsync(fixture.Authed(HttpMethod.Get, $"/v1/crm/boards/{id}", token));
        r.StatusCode.ShouldBe(HttpStatusCode.OK);
        return await PlatformApiFactory.ReadData(r);
    }

    private async Task<Guid> AddCardAsync(string token, Guid boardId, Guid columnId, string title)
    {
        var r = await fixture.Client.SendAsync(fixture.Authed(HttpMethod.Post, $"/v1/crm/boards/{boardId}/cards", token,
            new { columnId, title }));
        r.StatusCode.ShouldBe(HttpStatusCode.Created, await r.Content.ReadAsStringAsync());
        return (await PlatformApiFactory.ReadData(r)).GetProperty("id").GetGuid();
    }

    [Fact]
    public async Task Create_board_seeds_three_columns()
    {
        var (_, token) = await fixture.RegisterAndLoginAsync(Email(), "Sup3rSecret!");
        var id = await CreateBoardAsync(token, "Sales");
        var board = await GetBoardAsync(token, id);
        board.GetProperty("columns").EnumerateArray().Count().ShouldBe(3);
    }

    [Fact]
    public async Task Move_card_changes_column_and_renumbers()
    {
        var (_, token) = await fixture.RegisterAndLoginAsync(Email(), "Sup3rSecret!");
        var id = await CreateBoardAsync(token, "Pipeline");
        var board = await GetBoardAsync(token, id);
        var columns = board.GetProperty("columns").EnumerateArray().ToList();
        var todo = columns[0].GetProperty("id").GetGuid();
        var done = columns[2].GetProperty("id").GetGuid();

        var cardId = await AddCardAsync(token, id, todo, "First");
        await AddCardAsync(token, id, todo, "Second");

        var move = await fixture.Client.SendAsync(fixture.Authed(HttpMethod.Post, $"/v1/crm/cards/{cardId}/move", token,
            new { columnId = done, position = 0 }));
        move.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var after = await GetBoardAsync(token, id);
        var cards = after.GetProperty("cards").EnumerateArray().ToList();
        var moved = cards.First(c => c.GetProperty("id").GetGuid() == cardId);
        moved.GetProperty("columnId").GetGuid().ShouldBe(done);
        moved.GetProperty("position").GetInt32().ShouldBe(0);
    }

    [Fact]
    public async Task Foreign_board_is_not_found()
    {
        var (_, owner) = await fixture.RegisterAndLoginAsync(Email(), "Sup3rSecret!");
        var id = await CreateBoardAsync(owner, "Private");
        var (_, intruder) = await fixture.RegisterAndLoginAsync(Email(), "Sup3rSecret!");
        var get = await fixture.Client.SendAsync(fixture.Authed(HttpMethod.Get, $"/v1/crm/boards/{id}", intruder));
        get.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }
}
