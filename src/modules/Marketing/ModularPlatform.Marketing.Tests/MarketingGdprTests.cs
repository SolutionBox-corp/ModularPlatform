using System.Net;
using ModularPlatform.IntegrationTesting;
using Shouldly;

namespace ModularPlatform.Marketing.Tests;

/// <summary>
/// Marketing's participation in the platform GDPR fan-out, driven through the SAME real HTTP routes the Gdpr module
/// exposes (mirrors <c>GdprIntegrationTests</c>): the export query fans out every <see cref="ModularPlatform.Abstractions.IExportPersonalData"/>
/// into one document keyed by module — so a user who has Marketing data (a vibe-chat thread + message, and a completed
/// GA4 pull → snapshots → analysis) gets a populated <c>Marketing</c> section; the erasure pipeline fans out every
/// <see cref="ModularPlatform.Abstractions.IErasePersonalData"/> — Marketing DELETES the subject's rows outright (no
/// AML/tax retention), so afterwards none of the subject's marketing tables hold a row. Marketing is in
/// <c>DefaultEntitledModules</c> + <c>Marketing:UseFakeGateways=true</c>; SoloMode drains the durable worker in-process.
/// </summary>
[Collection("Integration")]
public sealed class MarketingGdprTests(PlatformApiFactory fixture)
{
    private const string Password = "Sup3rSecret!";

    [Fact]
    public async Task Export_includes_the_marketing_section_and_erase_removes_every_marketing_row()
    {
        var email = $"mkt-gdpr-{Guid.CreateVersion7():N}@example.com";
        var (userId, token) = await fixture.RegisterAndLoginAsync(email, Password);

        // ----- Seed Marketing data the subject owns -----------------------------------------------------------

        // A vibe-chat thread + a user message (the assistant reply is appended by the durable worker — polled below).
        var startConversation = await fixture.Client.SendAsync(
            fixture.Authed(HttpMethod.Post, "/v1/marketing/vibe/conversations", token, new { title = "GDPR seed" }));
        startConversation.StatusCode.ShouldBe(HttpStatusCode.Created);
        var conversationId = (await PlatformApiFactory.ReadData(startConversation)).GetProperty("conversationId").GetGuid();

        var sendMessage = await fixture.Client.SendAsync(
            fixture.Authed(HttpMethod.Post, $"/v1/marketing/vibe/conversations/{conversationId}/messages", token,
                new { content = "Summarize my campaign performance." }));
        sendMessage.StatusCode.ShouldBe(HttpStatusCode.Accepted);

        // A GA4 pull (202 → durable worker → snapshots + analysis). Poll the status until it reaches a terminal state.
        var startPull = await fixture.Client.SendAsync(
            fixture.Authed(HttpMethod.Post, "/v1/marketing/pulls", token, new { source = "ga4" }));
        startPull.StatusCode.ShouldBe(HttpStatusCode.Accepted);
        var dataPullId = (await PlatformApiFactory.ReadData(startPull)).GetProperty("dataPullId").GetGuid();

        var status = "Pending";
        for (var attempt = 0; attempt < 60 && status is "Pending" or "Running"; attempt++)
        {
            var poll = await fixture.Client.SendAsync(
                fixture.Authed(HttpMethod.Get, $"/v1/marketing/pulls/{dataPullId}", token));
            poll.StatusCode.ShouldBe(HttpStatusCode.OK);
            status = (await PlatformApiFactory.ReadData(poll)).GetProperty("status").GetString()!;
            if (status is "Pending" or "Running")
            {
                await Task.Delay(500);
            }
        }

        status.ShouldBe("Completed");

        // The completed pull triggers the (fake) analysis worker — wait until an analysis row exists for the subject.
        await fixture.WaitForCountAsync(
            $"""SELECT count(*)::bigint FROM marketing_analyses WHERE "UserId" = '{userId}'""", 1);

        // Wait until the durable agent turn has appended the assistant reply (so the export has the full thread).
        await fixture.WaitForCountAsync(
            $"""SELECT count(*)::bigint FROM vibe_messages WHERE "UserId" = '{userId}' AND "Role" = 'assistant'""", 1);

        // Stable GDPR export ordering: seed three same-timestamp rows per exported marketing section that is safe
        // to seed directly (vibe messages are encrypted at rest, so those stay covered by the real message flow).
        var secondConversation = await fixture.Client.SendAsync(
            fixture.Authed(HttpMethod.Post, "/v1/marketing/vibe/conversations", token, new { title = "GDPR seed 2" }));
        secondConversation.StatusCode.ShouldBe(HttpStatusCode.Created);
        var secondConversationId = (await PlatformApiFactory.ReadData(secondConversation))
            .GetProperty("conversationId").GetGuid();
        var thirdConversation = await fixture.Client.SendAsync(
            fixture.Authed(HttpMethod.Post, "/v1/marketing/vibe/conversations", token, new { title = "GDPR seed 3" }));
        thirdConversation.StatusCode.ShouldBe(HttpStatusCode.Created);
        var thirdConversationId = (await PlatformApiFactory.ReadData(thirdConversation))
            .GetProperty("conversationId").GetGuid();

        var stablePullIds = new[] { dataPullId, Guid.CreateVersion7(), Guid.CreateVersion7() };
        var stableSnapshotIds = new[] { Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7() };
        var stableAnalysisIds = new[] { Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7() };
        var stableConversationIds = new[] { conversationId, secondConversationId, thirdConversationId };
        const string stableInstant = "2030-01-01 00:00:00+00";

        await fixture.ExecuteSqlAsync(
            "UPDATE data_pulls " +
            $"SET \"CreatedAt\" = timestamp with time zone '{stableInstant}' " +
            $"WHERE \"Id\" = '{dataPullId}'");
        foreach (var id in stablePullIds.Skip(1))
        {
            await fixture.ExecuteSqlAsync(
                "INSERT INTO data_pulls (\"Id\", \"UserId\", \"Source\", \"Status\", \"CreatedAt\") " +
                $"VALUES ('{id}', '{userId}', 'Ga4', 'Completed', timestamp with time zone '{stableInstant}')");
        }

        foreach (var id in stableSnapshotIds)
        {
            await fixture.ExecuteSqlAsync(
                "INSERT INTO metric_snapshots (\"Id\", \"UserId\", \"DataPullId\", \"Source\", \"MetricName\", \"Value\", \"RecordedAt\") " +
                $"VALUES ('{id}', '{userId}', '{dataPullId}', 'Ga4', 'ga4:stable', 42, timestamp with time zone '{stableInstant}')");
        }

        foreach (var id in stableAnalysisIds)
        {
            await fixture.ExecuteSqlAsync(
                "INSERT INTO marketing_analyses (\"Id\", \"UserId\", \"DataPullId\", \"Source\", \"Summary\", \"AnalyzedAt\", \"CreatedAt\") " +
                $"VALUES ('{id}', '{userId}', '{dataPullId}', 'Ga4', 'Stable analysis', timestamp with time zone '{stableInstant}', timestamp with time zone '{stableInstant}')");
        }

        await fixture.ExecuteSqlAsync(
            "UPDATE vibe_conversations " +
            $"SET \"CreatedAt\" = timestamp with time zone '{stableInstant}' " +
            $"WHERE \"Id\" IN ('{conversationId}', '{secondConversationId}', '{thirdConversationId}')");

        // ----- EXPORT: the Marketing section is present and populated -----------------------------------------

        var export = await fixture.Client.SendAsync(
            fixture.Authed(HttpMethod.Get, "/v1/gdpr/me/export", token));
        export.StatusCode.ShouldBe(HttpStatusCode.OK);

        var document = await PlatformApiFactory.ReadData(export);
        document.TryGetProperty("Marketing", out var marketing).ShouldBeTrue("the export must carry the Marketing section");

        // Each named subsection from MarketingPersonalDataExporter carries the subject's rows.
        marketing.GetProperty("vibe_conversations").GetArrayLength().ShouldBeGreaterThanOrEqualTo(1);
        marketing.GetProperty("vibe_messages").GetArrayLength().ShouldBeGreaterThanOrEqualTo(1);
        marketing.GetProperty("pulls").GetArrayLength().ShouldBeGreaterThanOrEqualTo(1);
        marketing.GetProperty("snapshots").GetArrayLength().ShouldBeGreaterThanOrEqualTo(1);
        marketing.GetProperty("analyses").GetArrayLength().ShouldBeGreaterThanOrEqualTo(1);

        // The exported conversation is the one we created.
        marketing.GetProperty("vibe_conversations").EnumerateArray()
            .Select(c => c.GetProperty("id").GetGuid())
            .ShouldContain(conversationId);
        marketing.GetProperty("pulls").EnumerateArray()
            .Take(3)
            .Select(p => p.GetProperty("id").GetGuid())
            .ShouldBe(stablePullIds.OrderByDescending(id => id).ToArray());
        marketing.GetProperty("snapshots").EnumerateArray()
            .Take(3)
            .Select(s => s.GetProperty("id").GetGuid())
            .ShouldBe(stableSnapshotIds.OrderByDescending(id => id).ToArray());
        marketing.GetProperty("analyses").EnumerateArray()
            .Take(3)
            .Select(a => a.GetProperty("id").GetGuid())
            .ShouldBe(stableAnalysisIds.OrderByDescending(id => id).ToArray());
        marketing.GetProperty("vibe_conversations").EnumerateArray()
            .Take(3)
            .Select(c => c.GetProperty("id").GetGuid())
            .ShouldBe(stableConversationIds.OrderByDescending(id => id).ToArray());

        // ----- ERASE: every Marketing row for the subject is removed (durable fan-out → poll to zero) ---------

        var erase = await fixture.Client.SendAsync(
            fixture.Authed(HttpMethod.Post, "/v1/gdpr/me/erase", token));
        erase.StatusCode.ShouldBe(HttpStatusCode.OK);

        // The erasure fan-out is durable + async — poll until every Marketing table holds zero rows for the subject.
        // (WaitForCountAsync uses ">= expected", so to wait for emptiness we wait for an "is now empty" flag to be 1.)
        await WaitForEmptyAsync("vibe_messages", userId);
        // The conversation eraser ignores the soft-delete filter, so a hidden thread is not left behind either.
        await WaitForEmptyAsync("vibe_conversations", userId);
        await WaitForEmptyAsync("marketing_analyses", userId);
        await WaitForEmptyAsync("metric_snapshots", userId);
        await WaitForEmptyAsync("data_pulls", userId);
    }

    /// <summary>
    /// Polls until the given table holds NO rows for the subject. <see cref="PlatformApiFactory.WaitForCountAsync"/>
    /// asserts <c>count &gt;= expected</c>, which a literal <c>0</c> satisfies immediately — so we instead wait for an
    /// "is empty" flag (<c>1</c> when the count has reached zero) to genuinely observe the deletion.
    /// </summary>
    private async Task WaitForEmptyAsync(string table, Guid userId) =>
        await fixture.WaitForCountAsync(
            $"""SELECT CASE WHEN count(*) = 0 THEN 1 ELSE 0 END::bigint FROM {table} WHERE "UserId" = '{userId}'""", 1);
}
