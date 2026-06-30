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
        var columns = board.GetProperty("columns").EnumerateArray().ToList();
        columns.Count.ShouldBe(4);
        columns[0].GetProperty("group").GetString().ShouldBe("backlog");
        columns[1].GetProperty("isDefault").GetBoolean().ShouldBeTrue();
        columns[2].GetProperty("color").GetString().ShouldBe("#F59E0B");
    }

    [Fact]
    public async Task Move_card_changes_column_and_renumbers()
    {
        var (_, token) = await fixture.RegisterAndLoginAsync(Email(), "Sup3rSecret!");
        var id = await CreateBoardAsync(token, "Pipeline");
        var board = await GetBoardAsync(token, id);
        var columns = board.GetProperty("columns").EnumerateArray().ToList();
        var todo = columns[0].GetProperty("id").GetGuid();
        var done = columns[3].GetProperty("id").GetGuid();

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
            HttpMethod.Post, "/v1/crm/contacts", tokenA, new { firstName = "A", lastName = "Contact", status = "new" }));
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
        board.GetProperty("columns").EnumerateArray().Count().ShouldBe(5);
    }

    [Fact]
    public async Task Create_card_round_trips_rich_metadata()
    {
        var (_, token) = await fixture.RegisterAndLoginAsync(Email(), "Sup3rSecret!");
        var boardId = await CreateBoardAsync(token, "Rich");
        var board = await GetBoardAsync(token, boardId);
        var columnId = board.GetProperty("columns").EnumerateArray().First(c => c.GetProperty("isDefault").GetBoolean()).GetProperty("id").GetGuid();
        var start = DateTimeOffset.UtcNow.Date.AddDays(1);
        var due = start.AddDays(2);
        var assigneeId = Guid.CreateVersion7();

        var create = await fixture.Client.SendAsync(fixture.Authed(
            HttpMethod.Post, $"/v1/crm/boards/{boardId}/cards", token,
            new
            {
                columnId,
                title = "Call buyer",
                description = "Prepare next step",
                priority = "high",
                labels = new[] { "VIP", "vip", " proposal " },
                assigneeUserId = assigneeId,
                startAt = start,
                dueAt = due,
            }));
        create.StatusCode.ShouldBe(HttpStatusCode.Created, await create.Content.ReadAsStringAsync());
        var cardId = (await PlatformApiFactory.ReadData(create)).GetProperty("id").GetGuid();

        var after = await GetBoardAsync(token, boardId);
        var card = after.GetProperty("cards").EnumerateArray().Single(c => c.GetProperty("id").GetGuid() == cardId);
        card.GetProperty("priority").GetString().ShouldBe("high");
        card.GetProperty("assigneeUserId").GetGuid().ShouldBe(assigneeId);
        card.GetProperty("labels").EnumerateArray().Select(x => x.GetString()).ShouldBe(["VIP", "proposal"]);
        card.GetProperty("startAt").GetDateTimeOffset().ShouldBe(start);
        card.GetProperty("dueAt").GetDateTimeOffset().ShouldBe(due);
    }

    [Fact]
    public async Task Update_card_changes_existing_metadata()
    {
        var (_, token) = await fixture.RegisterAndLoginAsync(Email(), "Sup3rSecret!");
        var boardId = await CreateBoardAsync(token, "Editable");
        var board = await GetBoardAsync(token, boardId);
        var columnId = board.GetProperty("columns").EnumerateArray().First(c => c.GetProperty("isDefault").GetBoolean()).GetProperty("id").GetGuid();
        var cardId = await AddCardAsync(token, boardId, columnId, "Old title");
        var task = await fixture.Client.SendAsync(fixture.Authed(
            HttpMethod.Post, "/v1/crm/tasks", token,
            new { title = "Old task", description = "Task body", priority = "low" }));
        task.StatusCode.ShouldBe(HttpStatusCode.Created, await task.Content.ReadAsStringAsync());
        var taskId = (await PlatformApiFactory.ReadData(task)).GetProperty("id").GetGuid();
        var due = DateTimeOffset.UtcNow.Date.AddDays(3);

        var update = await fixture.Client.SendAsync(fixture.Authed(
            HttpMethod.Patch, $"/v1/crm/cards/{cardId}", token,
            new { title = "New title", description = "Edited", priority = "high", labels = new[] { "vip", "proposal" }, taskId, dueAt = due }));
        update.StatusCode.ShouldBe(HttpStatusCode.OK, await update.Content.ReadAsStringAsync());
        var data = await PlatformApiFactory.ReadData(update);
        data.GetProperty("title").GetString().ShouldBe("New title");
        data.GetProperty("description").GetString().ShouldBe("Edited");
        data.GetProperty("priority").GetString().ShouldBe("high");
        data.GetProperty("taskId").GetGuid().ShouldBe(taskId);
        data.GetProperty("labels").EnumerateArray().Select(x => x.GetString()).ShouldBe(["vip", "proposal"]);
        data.GetProperty("dueAt").GetDateTimeOffset().ShouldBe(due);

        var getTask = await fixture.Client.SendAsync(fixture.Authed(HttpMethod.Get, $"/v1/crm/tasks/{taskId}", token));
        var taskData = await PlatformApiFactory.ReadData(getTask);
        taskData.GetProperty("title").GetString().ShouldBe("New title");
        taskData.GetProperty("description").GetString().ShouldBe("Edited");
        taskData.GetProperty("priority").GetString().ShouldBe("high");
        taskData.GetProperty("dueAt").GetDateTimeOffset().ShouldBe(due);
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
