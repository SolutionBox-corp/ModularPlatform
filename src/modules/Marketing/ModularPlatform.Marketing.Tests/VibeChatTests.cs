using System.Net;
using System.Text.Json;
using ModularPlatform.IntegrationTesting;
using Shouldly;

namespace ModularPlatform.Marketing.Tests;

/// <summary>
/// The vibe-chat slice end-to-end on the shared harness: start a conversation, send a message (202 + durable agent
/// turn), poll until the (fake) gateway's assistant reply lands — assert it carries text + a tool-call trace. Plus the
/// input-validation guards (empty / oversized content), RLS owner-scoping (a second user can neither read nor write the
/// first user's thread, nor see it in their list), and the soft-delete (a deleted thread vanishes from the list).
/// Marketing is in <c>DefaultEntitledModules</c> so a freshly-registered user is entitled; <c>Marketing:UseFakeGateways=true</c>
/// in the harness makes the agent deterministic + instant; SoloMode drains the durable worker turn in-process while polling.
/// </summary>
[Collection("Integration")]
public sealed class VibeChatTests(PlatformApiFactory fixture)
{
    private const string Password = "Sup3rSecret!";

    [Fact]
    public async Task Start_send_and_the_durable_worker_persists_an_assistant_reply_with_a_tool_call_trace()
    {
        var (_, token) = await fixture.RegisterAndLoginAsync($"mkt-vibe-{Guid.CreateVersion7():N}@x.com", Password);

        // Start: POST creates the conversation (201 + Location + the new id in the body).
        var start = await fixture.Client.SendAsync(
            fixture.Authed(HttpMethod.Post, "/v1/marketing/vibe/conversations", token, new { title = "Q3" }));
        start.StatusCode.ShouldBe(HttpStatusCode.Created);
        var conversationId = (await PlatformApiFactory.ReadData(start)).GetProperty("conversationId").GetGuid();
        start.Headers.Location!.ToString().ShouldContain(conversationId.ToString());

        // Send: 202 — the user turn is persisted + the durable agent turn is kicked off (the LLM loop never runs in the request).
        var send = await fixture.Client.SendAsync(
            fixture.Authed(HttpMethod.Post, $"/v1/marketing/vibe/conversations/{conversationId}/messages", token,
                new { content = "How is my GA4 traffic?" }));
        send.StatusCode.ShouldBe(HttpStatusCode.Accepted);

        // Poll the conversation until the durable worker appends the assistant reply.
        JsonElement assistant = default;
        var found = false;
        for (var attempt = 0; attempt < 60 && !found; attempt++)
        {
            var poll = await fixture.Client.SendAsync(
                fixture.Authed(HttpMethod.Get, $"/v1/marketing/vibe/conversations/{conversationId}", token));
            poll.StatusCode.ShouldBe(HttpStatusCode.OK);

            var detail = await PlatformApiFactory.ReadData(poll);
            foreach (var message in detail.GetProperty("messages").EnumerateArray())
            {
                if (message.GetProperty("role").GetString() == "assistant")
                {
                    assistant = message;
                    found = true;
                    break;
                }
            }

            if (!found)
            {
                await Task.Delay(500);
            }
        }

        found.ShouldBeTrue("the durable worker should have appended an assistant reply");

        // The assistant turn carries non-empty text and a non-null tool-call trace (the fake emits one mock tool call).
        assistant.GetProperty("content").GetString().ShouldNotBeNullOrEmpty();
        assistant.GetProperty("toolCallsJson").ValueKind.ShouldBe(JsonValueKind.String);
        var toolCalls = assistant.GetProperty("toolCallsJson").GetString();
        toolCalls.ShouldNotBeNull();
        toolCalls.ShouldContain("list_recent_pulls");

        var rawUserContent = await fixture.ScalarAsync<string>(
            $"SELECT \"Content\" FROM vibe_messages WHERE \"ConversationId\" = '{conversationId}' AND \"Role\" = 'user' LIMIT 1");
        rawUserContent.ShouldStartWith("penc:v2:");

        var rawAssistantContent = await fixture.ScalarAsync<string>(
            $"SELECT \"Content\" FROM vibe_messages WHERE \"ConversationId\" = '{conversationId}' AND \"Role\" = 'assistant' LIMIT 1");
        rawAssistantContent.ShouldStartWith("penc:v2:");

        var rawToolCalls = await fixture.ScalarAsync<string>(
            $"SELECT \"ToolCallsJson\" FROM vibe_messages WHERE \"ConversationId\" = '{conversationId}' AND \"Role\" = 'assistant' LIMIT 1");
        rawToolCalls.ShouldStartWith("penc:v2:");

        // The conversation is listed for the owner.
        var list = await fixture.Client.SendAsync(
            fixture.Authed(HttpMethod.Get, "/v1/marketing/vibe/conversations", token));
        list.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await PlatformApiFactory.ReadData(list)).GetProperty("items").EnumerateArray()
            .Select(c => c.GetProperty("id").GetGuid())
            .ShouldContain(conversationId);
    }

    [Fact]
    public async Task Empty_message_is_rejected_with_message_required()
    {
        var (_, token) = await fixture.RegisterAndLoginAsync($"mkt-vibe-empty-{Guid.CreateVersion7():N}@x.com", Password);

        var conversationId = await StartConversationAsync(token);

        var send = await fixture.Client.SendAsync(
            fixture.Authed(HttpMethod.Post, $"/v1/marketing/vibe/conversations/{conversationId}/messages", token,
                new { content = "" }));

        send.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await send.Content.ReadAsStringAsync()).ShouldContain("marketing.vibe.message_required");
    }

    [Fact]
    public async Task Oversized_message_is_rejected_with_message_too_long()
    {
        var (_, token) = await fixture.RegisterAndLoginAsync($"mkt-vibe-long-{Guid.CreateVersion7():N}@x.com", Password);

        var conversationId = await StartConversationAsync(token);

        var send = await fixture.Client.SendAsync(
            fixture.Authed(HttpMethod.Post, $"/v1/marketing/vibe/conversations/{conversationId}/messages", token,
                new { content = new string('a', 4001) }));

        send.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await send.Content.ReadAsStringAsync()).ShouldContain("marketing.vibe.message_too_long");
    }

    [Fact]
    public async Task A_second_user_cannot_read_send_to_or_see_the_first_users_conversation()
    {
        var (_, ownerToken) = await fixture.RegisterAndLoginAsync($"mkt-vibe-owner-{Guid.CreateVersion7():N}@x.com", Password);
        var conversationId = await StartConversationAsync(ownerToken);

        var (_, intruderToken) = await fixture.RegisterAndLoginAsync($"mkt-vibe-intruder-{Guid.CreateVersion7():N}@x.com", Password);

        // The intruder cannot GET the owner's thread — RLS-scoped → 404 with the conversation_not_found code.
        var foreignGet = await fixture.Client.SendAsync(
            fixture.Authed(HttpMethod.Get, $"/v1/marketing/vibe/conversations/{conversationId}", intruderToken));
        foreignGet.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await foreignGet.Content.ReadAsStringAsync()).ShouldContain("marketing.vibe.conversation_not_found");

        // Nor send to it.
        var foreignSend = await fixture.Client.SendAsync(
            fixture.Authed(HttpMethod.Post, $"/v1/marketing/vibe/conversations/{conversationId}/messages", intruderToken,
                new { content = "let me in" }));
        foreignSend.StatusCode.ShouldBe(HttpStatusCode.NotFound);

        // Nor see it in their own list.
        var intruderList = await fixture.Client.SendAsync(
            fixture.Authed(HttpMethod.Get, "/v1/marketing/vibe/conversations", intruderToken));
        intruderList.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await PlatformApiFactory.ReadData(intruderList)).GetProperty("items").EnumerateArray()
            .Select(c => c.GetProperty("id").GetGuid())
            .ShouldNotContain(conversationId);
    }

    [Fact]
    public async Task Deleting_a_conversation_removes_it_from_the_owners_list()
    {
        var (_, token) = await fixture.RegisterAndLoginAsync($"mkt-vibe-del-{Guid.CreateVersion7():N}@x.com", Password);
        var conversationId = await StartConversationAsync(token);

        // Present before deletion.
        var before = await fixture.Client.SendAsync(
            fixture.Authed(HttpMethod.Get, "/v1/marketing/vibe/conversations", token));
        (await PlatformApiFactory.ReadData(before)).GetProperty("items").EnumerateArray()
            .Select(c => c.GetProperty("id").GetGuid())
            .ShouldContain(conversationId);

        // Soft-delete.
        var delete = await fixture.Client.SendAsync(
            fixture.Authed(HttpMethod.Delete, $"/v1/marketing/vibe/conversations/{conversationId}", token));
        delete.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        // Gone from the list (soft-deleted threads are filtered out).
        var after = await fixture.Client.SendAsync(
            fixture.Authed(HttpMethod.Get, "/v1/marketing/vibe/conversations", token));
        after.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await PlatformApiFactory.ReadData(after)).GetProperty("items").EnumerateArray()
            .Select(c => c.GetProperty("id").GetGuid())
            .ShouldNotContain(conversationId);
    }

    [Fact]
    public async Task Conversation_list_and_thread_messages_are_paged()
    {
        var (_, token) = await fixture.RegisterAndLoginAsync($"mkt-vibe-page-{Guid.CreateVersion7():N}@x.com", Password);
        var first = await StartConversationAsync(token);
        await Task.Delay(10);
        var second = await StartConversationAsync(token);

        var list = await fixture.Client.SendAsync(
            fixture.Authed(HttpMethod.Get, "/v1/marketing/vibe/conversations?page=1&pageSize=1", token));
        list.StatusCode.ShouldBe(HttpStatusCode.OK);
        var listData = await PlatformApiFactory.ReadData(list);
        listData.GetProperty("page").GetInt32().ShouldBe(1);
        listData.GetProperty("pageSize").GetInt32().ShouldBe(1);
        listData.GetProperty("totalCount").GetInt64().ShouldBeGreaterThanOrEqualTo(2);
        listData.GetProperty("items").GetArrayLength().ShouldBe(1);
        listData.GetProperty("items")[0].GetProperty("id").GetGuid().ShouldBe(second);

        for (var i = 0; i < 3; i++)
        {
            var send = await fixture.Client.SendAsync(
                fixture.Authed(HttpMethod.Post, $"/v1/marketing/vibe/conversations/{first}/messages", token,
                    new { content = $"message {i}" }));
            send.StatusCode.ShouldBe(HttpStatusCode.Accepted);
        }

        var detail = await fixture.Client.SendAsync(
            fixture.Authed(HttpMethod.Get, $"/v1/marketing/vibe/conversations/{first}?messagePage=1&messagePageSize=2", token));
        detail.StatusCode.ShouldBe(HttpStatusCode.OK);
        var detailData = await PlatformApiFactory.ReadData(detail);
        detailData.GetProperty("messagePage").GetInt32().ShouldBe(1);
        detailData.GetProperty("messagePageSize").GetInt32().ShouldBe(2);
        detailData.GetProperty("totalMessageCount").GetInt64().ShouldBeGreaterThanOrEqualTo(3);
        detailData.GetProperty("messages").GetArrayLength().ShouldBe(2);
    }

    [Fact]
    public async Task Sending_a_message_moves_the_conversation_to_the_top_of_the_list()
    {
        var (_, token) = await fixture.RegisterAndLoginAsync($"mkt-vibe-active-{Guid.CreateVersion7():N}@x.com", Password);
        var first = await StartConversationAsync(token);
        await Task.Delay(10);
        var second = await StartConversationAsync(token);

        var before = await fixture.Client.SendAsync(
            fixture.Authed(HttpMethod.Get, "/v1/marketing/vibe/conversations?page=1&pageSize=2", token));
        before.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await PlatformApiFactory.ReadData(before)).GetProperty("items")[0].GetProperty("id").GetGuid().ShouldBe(second);

        var send = await fixture.Client.SendAsync(
            fixture.Authed(HttpMethod.Post, $"/v1/marketing/vibe/conversations/{first}/messages", token,
                new { content = "bring this thread back to the top" }));
        send.StatusCode.ShouldBe(HttpStatusCode.Accepted);

        var after = await fixture.Client.SendAsync(
            fixture.Authed(HttpMethod.Get, "/v1/marketing/vibe/conversations?page=1&pageSize=2", token));
        after.StatusCode.ShouldBe(HttpStatusCode.OK);
        var firstItem = (await PlatformApiFactory.ReadData(after)).GetProperty("items")[0];
        firstItem.GetProperty("id").GetGuid().ShouldBe(first);
        firstItem.GetProperty("lastMessageAt").GetDateTimeOffset().ShouldBeGreaterThan(DateTimeOffset.MinValue);
    }

    private async Task<Guid> StartConversationAsync(string token)
    {
        var start = await fixture.Client.SendAsync(
            fixture.Authed(HttpMethod.Post, "/v1/marketing/vibe/conversations", token, new { title = "Q3" }));
        start.StatusCode.ShouldBe(HttpStatusCode.Created);
        return (await PlatformApiFactory.ReadData(start)).GetProperty("conversationId").GetGuid();
    }
}
