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

    [Fact]
    public async Task Move_card_into_non_empty_target_lands_at_requested_position()
    {
        var (_, token) = await fixture.RegisterAndLoginAsync(Email(), "Sup3rSecret!");
        var boardId = await CreateBoardAsync(token, "Regression");
        var board = await GetBoardAsync(token, boardId);
        var cols = board.GetProperty("columns").EnumerateArray().ToList();
        var sourceId = cols[0].GetProperty("id").GetGuid();
        var targetId = cols[1].GetProperty("id").GetGuid();

        var cardAId = await AddCardAsync(token, boardId, sourceId, "A");
        var cardBId = await AddCardAsync(token, boardId, sourceId, "B");
        var cardCId = await AddCardAsync(token, boardId, sourceId, "C");
        await AddCardAsync(token, boardId, targetId, "X");
        await AddCardAsync(token, boardId, targetId, "Y");

        var move = await fixture.Client.SendAsync(fixture.Authed(
            HttpMethod.Post, $"/v1/crm/cards/{cardBId}/move", token,
            new { columnId = targetId, position = 1 }));
        move.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var after = await GetBoardAsync(token, boardId);
        var allCards = after.GetProperty("cards").EnumerateArray().ToList();

        var sourceCards = allCards
            .Where(c => c.GetProperty("columnId").GetGuid() == sourceId)
            .OrderBy(c => c.GetProperty("position").GetInt32())
            .ToList();
        sourceCards.Count.ShouldBe(2);
        sourceCards[0].GetProperty("title").GetString().ShouldBe("A");
        sourceCards[0].GetProperty("position").GetInt32().ShouldBe(0);
        sourceCards[1].GetProperty("title").GetString().ShouldBe("C");
        sourceCards[1].GetProperty("position").GetInt32().ShouldBe(1);

        var targetCards = allCards
            .Where(c => c.GetProperty("columnId").GetGuid() == targetId)
            .OrderBy(c => c.GetProperty("position").GetInt32())
            .ToList();
        targetCards.Count.ShouldBe(3);
        targetCards[0].GetProperty("title").GetString().ShouldBe("X");
        targetCards[0].GetProperty("position").GetInt32().ShouldBe(0);
        targetCards[1].GetProperty("title").GetString().ShouldBe("B");
        targetCards[1].GetProperty("position").GetInt32().ShouldBe(1);
        targetCards[2].GetProperty("title").GetString().ShouldBe("Y");
        targetCards[2].GetProperty("position").GetInt32().ShouldBe(2);
    }

    [Fact]
    public async Task Create_card_with_foreign_contact_is_not_found()
    {
        var (_, tokenA) = await fixture.RegisterAndLoginAsync(Email(), "Sup3rSecret!");
        var contactResp = await fixture.Client.SendAsync(fixture.Authed(
            HttpMethod.Post, "/v1/crm/contacts", tokenA, new { fullName = "A", status = "lead" }));
        contactResp.StatusCode.ShouldBe(HttpStatusCode.Created);
        var contactAId = (await PlatformApiFactory.ReadData(contactResp)).GetProperty("id").GetGuid();

        var (_, tokenB) = await fixture.RegisterAndLoginAsync(Email(), "Sup3rSecret!");
        var boardId = await CreateBoardAsync(tokenB, "B-Board");
        var board = await GetBoardAsync(tokenB, boardId);
        var columnId = board.GetProperty("columns").EnumerateArray().First().GetProperty("id").GetGuid();

        var create = await fixture.Client.SendAsync(fixture.Authed(
            HttpMethod.Post, $"/v1/crm/boards/{boardId}/cards", tokenB,
            new { columnId, title = "x", contactId = contactAId }));
        create.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Create_column_adds_to_board()
    {
        var (_, token) = await fixture.RegisterAndLoginAsync(Email(), "Sup3rSecret!");
        var boardId = await CreateBoardAsync(token, "Expandable");

        var col = await fixture.Client.SendAsync(fixture.Authed(
            HttpMethod.Post, $"/v1/crm/boards/{boardId}/columns", token, new { name = "Backlog" }));
        col.StatusCode.ShouldBe(HttpStatusCode.Created);

        var board = await GetBoardAsync(token, boardId);
        board.GetProperty("columns").EnumerateArray().Count().ShouldBe(4);
    }

    [Fact]
    public async Task Delete_card_removes_it_from_board()
    {
        var (_, token) = await fixture.RegisterAndLoginAsync(Email(), "Sup3rSecret!");
        var boardId = await CreateBoardAsync(token, "Shrinkable");
        var board = await GetBoardAsync(token, boardId);
        var columnId = board.GetProperty("columns").EnumerateArray().First().GetProperty("id").GetGuid();
        var cardId = await AddCardAsync(token, boardId, columnId, "To Remove");

        var del = await fixture.Client.SendAsync(fixture.Authed(HttpMethod.Delete, $"/v1/crm/cards/{cardId}", token));
        del.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var after = await GetBoardAsync(token, boardId);
        var ids = after.GetProperty("cards").EnumerateArray().Select(c => c.GetProperty("id").GetGuid());
        ids.ShouldNotContain(cardId);
    }

    [Fact]
    public async Task Delete_board_returns_not_found_afterwards()
    {
        var (_, token) = await fixture.RegisterAndLoginAsync(Email(), "Sup3rSecret!");
        var boardId = await CreateBoardAsync(token, "Ephemeral");

        var del = await fixture.Client.SendAsync(fixture.Authed(HttpMethod.Delete, $"/v1/crm/boards/{boardId}", token));
        del.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var get = await fixture.Client.SendAsync(fixture.Authed(HttpMethod.Get, $"/v1/crm/boards/{boardId}", token));
        get.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task List_boards_contains_created_board()
    {
        var (_, token) = await fixture.RegisterAndLoginAsync(Email(), "Sup3rSecret!");
        var boardId = await CreateBoardAsync(token, "Listed");

        var list = await fixture.Client.SendAsync(fixture.Authed(HttpMethod.Get, "/v1/crm/boards", token));
        list.StatusCode.ShouldBe(HttpStatusCode.OK);
        var data = await PlatformApiFactory.ReadData(list);
        data.GetProperty("totalCount").GetInt32().ShouldBeGreaterThanOrEqualTo(1);
        var ids = data.GetProperty("items").EnumerateArray().Select(b => b.GetProperty("id").GetGuid());
        ids.ShouldContain(boardId);
    }
}
