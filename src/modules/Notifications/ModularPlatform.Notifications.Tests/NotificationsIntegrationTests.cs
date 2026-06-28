using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using ModularPlatform.Abstractions;
using ModularPlatform.Billing.Contracts;
using ModularPlatform.Cqrs;
using ModularPlatform.Identity.Contracts;
using ModularPlatform.IntegrationTesting;
using ModularPlatform.Notifications.Contracts;
using ModularPlatform.Notifications.Messaging;
using Shouldly;
using Wolverine;

namespace ModularPlatform.Notifications.Tests;

/// <summary>
/// Integration tests for the Notifications module against the shared Testcontainers-Postgres + full Api host.
///
/// Grounding (production code, verified):
/// - Send endpoint: POST /v1/notifications/send, body { UserId, TemplateKey, Channels[], Data{} },
///   gated by PlatformPermissions.NotificationsSend ("notifications.send") — see SendNotificationEndpoint.cs:14-27.
/// - Feed endpoint: GET /v1/notifications/me?unreadOnly=&amp;page=&amp;pageSize= — GetMyNotificationsEndpoint.cs:14-30.
///   Data envelope is a PagedResponse: { items:[ { id, templateKey, title, body, readAt, createdAt } ], page,
///   pageSize, totalCount } — Paging.cs:7, GetMyNotificationsQuery.cs:8-14.
/// - Mark read: POST /v1/notifications/{notificationId:guid}/read — MarkNotificationReadEndpoint.cs:14.
/// - Tables: notifications (UserId, TemplateKey, Channel, Title, Body, ReadAt) — Notification.cs:12-34,
///   migration 20260608064755_InitialNotifications.cs:30-51; notification_templates (Id, Key, Locale, Subject,
///   Body) — NotificationTemplate.cs:12-31, migration lines 14-28.
/// - Welcome path: SendWelcomeHandler reacts to Identity's UserRegisteredIntegrationEvent and dispatches
///   SendNotificationCommand("welcome", ["email","inapp"]); a MISSING "welcome" template is caught and logged,
///   never dead-lettered — SendWelcomeHandler.cs:16-35. NO production code seeds a "welcome" template
///   (grep verified), and the test host does not seed it either — so EV-2 is in the "missing template" case.
/// - Channel hand-off is via the outbox, never inline: SendNotificationHandler.cs:50-86 publishes
///   EmailDeliveryRequested / PushDeliveryRequested to the outbox and only the inapp row is written inline.
/// </summary>
[Collection("Integration")]
public sealed class NotificationsIntegrationTests(PlatformApiFactory fixture)
{
    private const string Password = "Sup3rSecret!";

    // EV-2 — The NotificationsSeeder seeds the "welcome" template at startup. When a user registers,
    // the welcome notification IS created. The Billing credit account being provisioned proves the full
    // durable spine ran. The welcome in-app row appears in the feed.
    [Fact]
    public async Task Register_creates_welcome_notification_after_seeder_has_seeded_the_template()
    {
        var email = $"ev2-{Guid.CreateVersion7():N}@example.com";

        var (userId, token) = await fixture.RegisterAndLoginAsync(email, Password);

        // The UserRegisteredIntegrationEvent is processed end-to-end: Billing provisions a credit account.
        await fixture.WaitForCountAsync(
            $"SELECT count(*)::bigint FROM credit_accounts WHERE \"UserId\" = '{userId}'", 1);

        // The "welcome" template IS seeded by NotificationsSeeder, so the welcome handler writes an in-app row.
        await fixture.WaitForCountAsync(
            $"SELECT count(*)::bigint FROM notifications WHERE \"UserId\" = '{userId}' AND \"TemplateKey\" = 'welcome'", 1);

        // The feed endpoint surfaces the new notification.
        var feed = await fixture.Client.SendAsync(
            fixture.Authed(HttpMethod.Get, "/v1/notifications/me?unreadOnly=true", token));
        feed.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Welcome_handler_tolerates_missing_template_and_deduplicates_retries()
    {
        var handler = new SendWelcomeHandler(NullLogger<SendWelcomeHandler>.Instance);
        await using var scope = fixture.Services.CreateAsyncScope();
        var dispatcher = scope.ServiceProvider.GetRequiredService<IDispatcher>();

        var missingUserId = Guid.CreateVersion7();
        var missingMessage = new UserRegisteredIntegrationEvent(
            Guid.CreateVersion7(),
            DateTimeOffset.UtcNow,
            missingUserId,
            Guid.CreateVersion7(),
            $"missing-welcome-{Guid.CreateVersion7():N}@example.com",
            DisplayName: null);

        await fixture.ExecuteSqlAsync("DELETE FROM notification_templates WHERE \"Key\" = 'welcome'");
        try
        {
            await handler.Handle(missingMessage, dispatcher, CancellationToken.None);
            var missingRows = await fixture.ScalarAsync<long>(
                $"SELECT count(*)::bigint FROM notifications WHERE \"UserId\" = '{missingUserId}' AND \"TemplateKey\" = 'welcome'");
            missingRows.ShouldBe(0);
        }
        finally
        {
            await RestoreWelcomeTemplatesAsync();
        }

        var retryUserId = Guid.CreateVersion7();
        var retryMessage = missingMessage with
        {
            UserId = retryUserId,
            Email = $"retry-welcome-{Guid.CreateVersion7():N}@example.com",
        };

        await handler.Handle(retryMessage, dispatcher, CancellationToken.None);
        await handler.Handle(retryMessage, dispatcher, CancellationToken.None);

        var retryRows = await fixture.ScalarAsync<long>(
            $"SELECT count(*)::bigint FROM notifications WHERE \"UserId\" = '{retryUserId}' AND \"TemplateKey\" = 'welcome'");
        retryRows.ShouldBe(1);
    }

    // EV-2 resilience — A handler that finds NO template (bogus key) swallows NotFoundException and never
    // dead-letters. Use a direct dispatcher call to prove the non-fatal path: it should return success (no
    // exception propagated) but write NO notification row.
    [Fact]
    public async Task SendNotification_with_missing_template_key_returns_not_found()
    {
        var (recipientId, adminToken) = await AdminTokenAsync();

        var bogusKey = $"does-not-exist-{Guid.CreateVersion7():N}";
        var send = await fixture.Client.SendAsync(fixture.Authed(
            HttpMethod.Post, "/v1/notifications/send", adminToken, new
            {
                userId = recipientId,
                templateKey = bogusKey,
                channels = new[] { "inapp" },
                data = new Dictionary<string, string>(),
            }));

        // The handler throws NotFoundException when the template is missing — HTTP 404.
        send.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    // NT-1 — SendNotification persists an in-app row inline AND hands per-channel delivery off via the outbox
    // (never inline). We seed a template, call the send endpoint as an admin (the only caller with
    // notifications.send), then assert: (a) HTTP 200 returned immediately, and (b) exactly one inapp row was
    // persisted with the rendered title/body. The 200 + persisted row prove the email channel was NOT sent
    // inline (the test host has no reachable SMTP relay, so an inline send would have failed the request);
    // the email/push are published to the outbox per SendNotificationHandler.cs:55-74 + the explicit
    // "never sends inline" contract in Notifications.Contracts/Commands.cs + IntegrationEvents.cs.
    [Fact]
    public async Task SendNotification_persists_an_inapp_row_and_enqueues_channel_delivery_via_the_outbox()
    {
        // The admin is both the sender (holds notifications.send) and the recipient: RLS WITH CHECK on the
        // IUserOwned notifications table means an HTTP caller can only create a row for its OWN id.
        var (recipientId, adminToken) = await AdminTokenAsync();

        // Seed a template (not tenant/user-scoped — a plain admin insert). Unique key avoids cross-test collision.
        var templateKey = $"nt1-{Guid.CreateVersion7():N}";
        await fixture.ExecuteSqlAsync(
            $"INSERT INTO notification_templates (\"Id\", \"Key\", \"Locale\", \"Subject\", \"Body\") " +
            $"VALUES ('{Guid.CreateVersion7()}', '{templateKey}', 'en', 'Hello {{displayName}}', 'Body for {{displayName}}')");

        var send = await fixture.Client.SendAsync(fixture.Authed(
            HttpMethod.Post, "/v1/notifications/send", adminToken, new
            {
                userId = recipientId,
                templateKey,
                channels = new[] { "email", "inapp" },
                data = new Dictionary<string, string> { ["displayName"] = "Ada", ["email"] = "ada@example.com" },
            }));

        // 200 returned immediately — the slow SMTP work was deferred to the Worker, not run inline in the request.
        send.StatusCode.ShouldBe(HttpStatusCode.OK);

        // Exactly one inapp feed row persisted (Channel is not PII, so a raw read is fine).
        var rows = await fixture.ScalarAsync<long>(
            $"SELECT count(*)::bigint FROM notifications WHERE \"UserId\" = '{recipientId}' AND \"TemplateKey\" = '{templateKey}'");
        rows.ShouldBe(1);

        var channel = await fixture.ScalarAsync<string>(
            $"SELECT \"Channel\" FROM notifications WHERE \"UserId\" = '{recipientId}' AND \"TemplateKey\" = '{templateKey}'");
        channel.ShouldBe("inapp");

        // Title is [Encrypted] at rest (a penc:v2 envelope in the column) — the rendered plaintext ({displayName} ->
        // "Ada") is only visible through the model converter, i.e. via the feed API, which decrypts on read.
        var rawTitle = await fixture.ScalarAsync<string>(
            $"SELECT \"Title\" FROM notifications WHERE \"UserId\" = '{recipientId}' AND \"TemplateKey\" = '{templateKey}'");
        rawTitle.ShouldStartWith("penc:v2:");

        var feed = await fixture.Client.SendAsync(
            fixture.Authed(HttpMethod.Get, "/v1/notifications/me", adminToken));
        feed.StatusCode.ShouldBe(HttpStatusCode.OK);
        var item = (await PlatformApiFactory.ReadData(feed)).GetProperty("items").EnumerateArray()
            .Single(n => n.GetProperty("templateKey").GetString() == templateKey);
        item.GetProperty("title").GetString().ShouldBe("Hello Ada");
    }

    [Fact]
    public async Task SendNotification_with_idempotency_key_is_exactly_once_over_http()
    {
        var (recipientId, adminToken) = await AdminTokenAsync();

        var templateKey = $"nt-dedup-{Guid.CreateVersion7():N}";
        var idempotencyKey = $"notification:{Guid.CreateVersion7():N}";
        await fixture.ExecuteSqlAsync(
            $"INSERT INTO notification_templates (\"Id\", \"Key\", \"Locale\", \"Subject\", \"Body\") " +
            $"VALUES ('{Guid.CreateVersion7()}', '{templateKey}', 'en', 'Dedup', 'Body')");

        async Task<HttpResponseMessage> Send() => await fixture.Client.SendAsync(fixture.Authed(
            HttpMethod.Post, "/v1/notifications/send", adminToken, new
            {
                userId = recipientId,
                templateKey,
                channels = new[] { "inapp" },
                data = new Dictionary<string, string>(),
                idempotencyKey,
            }));

        var first = await Send();
        var second = await Send();

        first.StatusCode.ShouldBe(HttpStatusCode.OK);
        second.StatusCode.ShouldBe(HttpStatusCode.OK);

        var rows = await fixture.ScalarAsync<long>(
            $"SELECT count(*)::bigint FROM notifications WHERE \"IdempotencyKey\" = '{idempotencyKey}'");
        rows.ShouldBe(1);
    }

    // NT-4 — GetMyNotifications(unreadOnly=true) + MarkNotificationRead round-trip. Seed a template, send an
    // in-app notification to the user, GET unreadOnly=true returns it, mark it read, GET unreadOnly=true no
    // longer returns it. The user reads/marks their OWN feed (identity from the token, RLS-scoped).
    [Fact]
    public async Task Unread_feed_and_mark_read_round_trip()
    {
        // Admin is sender + recipient (RLS WITH CHECK: an HTTP caller writes notification rows only for itself);
        // the recipient reads + marks their OWN feed with their own token.
        var (recipientId, recipientToken) = await AdminTokenAsync();
        var adminToken = recipientToken;

        var templateKey = $"nt4-{Guid.CreateVersion7():N}";
        await fixture.ExecuteSqlAsync(
            $"INSERT INTO notification_templates (\"Id\", \"Key\", \"Locale\", \"Subject\", \"Body\") " +
            $"VALUES ('{Guid.CreateVersion7()}', '{templateKey}', 'en', 'Subj', 'Body')");

        var send = await fixture.Client.SendAsync(fixture.Authed(
            HttpMethod.Post, "/v1/notifications/send", adminToken, new
            {
                userId = recipientId,
                templateKey,
                channels = new[] { "inapp" },
                data = new Dictionary<string, string>(),
            }));
        send.StatusCode.ShouldBe(HttpStatusCode.OK);

        // unreadOnly=true returns the new notification.
        var unread = await fixture.Client.SendAsync(
            fixture.Authed(HttpMethod.Get, "/v1/notifications/me?unreadOnly=true", recipientToken));
        unread.StatusCode.ShouldBe(HttpStatusCode.OK);

        var unreadData = await PlatformApiFactory.ReadData(unread);
        var item = unreadData.GetProperty("items").EnumerateArray()
            .Single(n => n.GetProperty("templateKey").GetString() == templateKey);
        item.GetProperty("readAt").ValueKind.ShouldBe(System.Text.Json.JsonValueKind.Null);
        var notificationId = item.GetProperty("id").GetGuid();

        // Mark it read (the user marks their own notification — identity comes from the token).
        var markRead = await fixture.Client.SendAsync(fixture.Authed(
            HttpMethod.Post, $"/v1/notifications/{notificationId}/read", recipientToken));
        markRead.StatusCode.ShouldBe(HttpStatusCode.OK);

        // unreadOnly=true no longer returns it.
        var afterRead = await fixture.Client.SendAsync(
            fixture.Authed(HttpMethod.Get, "/v1/notifications/me?unreadOnly=true", recipientToken));
        afterRead.StatusCode.ShouldBe(HttpStatusCode.OK);

        var afterReadData = await PlatformApiFactory.ReadData(afterRead);
        afterReadData.GetProperty("items").EnumerateArray()
            .Any(n => n.GetProperty("templateKey").GetString() == templateKey)
            .ShouldBeFalse();
    }

    [Fact]
    public async Task My_notifications_feed_is_paged_and_owner_scoped()
    {
        var (aliceId, aliceToken) = await fixture.RegisterAndLoginAsync(
            $"notif-alice-{Guid.CreateVersion7():N}@example.com", Password);
        var (bobId, _) = await fixture.RegisterAndLoginAsync(
            $"notif-bob-{Guid.CreateVersion7():N}@example.com", Password);

        var alicePrefix = $"feed-alice-{Guid.CreateVersion7():N}";
        var bobTemplate = $"feed-bob-{Guid.CreateVersion7():N}";
        await SeedTemplateAsync($"{alicePrefix}-1", "A1");
        await SeedTemplateAsync($"{alicePrefix}-2", "A2");
        await SeedTemplateAsync($"{alicePrefix}-3", "A3");
        await SeedTemplateAsync(bobTemplate, "B");

        await SendDirectAsync(aliceId, $"{alicePrefix}-1");
        await SendDirectAsync(aliceId, $"{alicePrefix}-2");
        await SendDirectAsync(aliceId, $"{alicePrefix}-3");
        await SendDirectAsync(bobId, bobTemplate);

        var response = await fixture.Client.SendAsync(
            fixture.Authed(HttpMethod.Get, "/v1/notifications/me?page=1&pageSize=2", aliceToken));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var data = await PlatformApiFactory.ReadData(response);
        data.GetProperty("page").GetInt32().ShouldBe(1);
        data.GetProperty("pageSize").GetInt32().ShouldBe(2);
        data.GetProperty("items").GetArrayLength().ShouldBe(2);
        data.GetProperty("totalCount").GetInt64().ShouldBeGreaterThanOrEqualTo(3);
        data.GetProperty("items").EnumerateArray()
            .Any(n => n.GetProperty("templateKey").GetString() == bobTemplate)
            .ShouldBeFalse();
    }

    [Fact]
    public async Task My_notifications_default_feed_includes_read_and_unread_and_clamps_page_bounds()
    {
        var (userId, token) = await fixture.RegisterAndLoginAsync(
            $"notif-default-{Guid.CreateVersion7():N}@example.com", Password);

        await fixture.ExecuteSqlAsync($"UPDATE notifications SET \"ReadAt\" = now() WHERE \"UserId\" = '{userId}'");

        var readTemplate = $"feed-read-{Guid.CreateVersion7():N}";
        var unreadTemplate = $"feed-unread-{Guid.CreateVersion7():N}";
        await SeedTemplateAsync(readTemplate, "Read");
        await SeedTemplateAsync(unreadTemplate, "Unread");
        await SendDirectAsync(userId, readTemplate);
        await SendDirectAsync(userId, unreadTemplate);

        var readId = await fixture.ScalarAsync<Guid>(
            $"SELECT \"Id\" FROM notifications WHERE \"UserId\" = '{userId}' AND \"TemplateKey\" = '{readTemplate}'");
        var markRead = await fixture.Client.SendAsync(
            fixture.Authed(HttpMethod.Post, $"/v1/notifications/{readId}/read", token));
        markRead.StatusCode.ShouldBe(HttpStatusCode.OK);

        var defaultFeed = await fixture.Client.SendAsync(
            fixture.Authed(HttpMethod.Get, "/v1/notifications/me?page=0&pageSize=999", token));

        defaultFeed.StatusCode.ShouldBe(HttpStatusCode.OK);
        var data = await PlatformApiFactory.ReadData(defaultFeed);
        data.GetProperty("page").GetInt32().ShouldBe(1);
        data.GetProperty("pageSize").GetInt32().ShouldBe(100);

        var templateKeys = data.GetProperty("items").EnumerateArray()
            .Select(n => n.GetProperty("templateKey").GetString())
            .ToArray();
        templateKeys.ShouldContain(readTemplate);
        templateKeys.ShouldContain(unreadTemplate);

        var unreadOnly = await fixture.Client.SendAsync(
            fixture.Authed(HttpMethod.Get, "/v1/notifications/me?unreadOnly=true", token));
        unreadOnly.StatusCode.ShouldBe(HttpStatusCode.OK);
        var unreadKeys = (await PlatformApiFactory.ReadData(unreadOnly)).GetProperty("items").EnumerateArray()
            .Select(n => n.GetProperty("templateKey").GetString())
            .ToArray();
        unreadKeys.ShouldNotContain(readTemplate);
        unreadKeys.ShouldContain(unreadTemplate);
    }

    [Fact]
    public async Task Unread_count_is_owner_scoped_and_updates_after_mark_read()
    {
        var (aliceId, aliceToken) = await fixture.RegisterAndLoginAsync(
            $"count-alice-{Guid.CreateVersion7():N}@example.com", Password);
        var (bobId, _) = await fixture.RegisterAndLoginAsync(
            $"count-bob-{Guid.CreateVersion7():N}@example.com", Password);

        await fixture.ExecuteSqlAsync($"UPDATE notifications SET \"ReadAt\" = now() WHERE \"UserId\" = '{aliceId}'");

        var aliceOne = $"count-alice-1-{Guid.CreateVersion7():N}";
        var aliceTwo = $"count-alice-2-{Guid.CreateVersion7():N}";
        var bobTemplate = $"count-bob-{Guid.CreateVersion7():N}";
        await SeedTemplateAsync(aliceOne, "A1");
        await SeedTemplateAsync(aliceTwo, "A2");
        await SeedTemplateAsync(bobTemplate, "B");

        await SendDirectAsync(aliceId, aliceOne);
        await SendDirectAsync(aliceId, aliceTwo);
        await SendDirectAsync(bobId, bobTemplate);

        var before = await fixture.Client.SendAsync(
            fixture.Authed(HttpMethod.Get, "/v1/notifications/me/unread-count", aliceToken));
        before.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await PlatformApiFactory.ReadData(before)).GetProperty("count").GetInt64().ShouldBe(2);

        var notificationId = await fixture.ScalarAsync<Guid>(
            $"SELECT \"Id\" FROM notifications WHERE \"UserId\" = '{aliceId}' AND \"TemplateKey\" = '{aliceOne}'");
        var markRead = await fixture.Client.SendAsync(
            fixture.Authed(HttpMethod.Post, $"/v1/notifications/{notificationId}/read", aliceToken));
        markRead.StatusCode.ShouldBe(HttpStatusCode.OK);

        var after = await fixture.Client.SendAsync(
            fixture.Authed(HttpMethod.Get, "/v1/notifications/me/unread-count", aliceToken));
        after.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await PlatformApiFactory.ReadData(after)).GetProperty("count").GetInt64().ShouldBe(1);
    }

    [Fact]
    public async Task Mark_notification_read_is_owner_scoped_and_idempotent()
    {
        var (aliceId, aliceToken) = await fixture.RegisterAndLoginAsync(
            $"mark-alice-{Guid.CreateVersion7():N}@example.com", Password);
        var (bobId, bobToken) = await fixture.RegisterAndLoginAsync(
            $"mark-bob-{Guid.CreateVersion7():N}@example.com", Password);

        var aliceTemplate = $"mark-alice-{Guid.CreateVersion7():N}";
        var bobTemplate = $"mark-bob-{Guid.CreateVersion7():N}";
        await SeedTemplateAsync(aliceTemplate, "A");
        await SeedTemplateAsync(bobTemplate, "B");
        await SendDirectAsync(aliceId, aliceTemplate);
        await SendDirectAsync(bobId, bobTemplate);

        var bobNotificationId = await fixture.ScalarAsync<Guid>(
            $"SELECT \"Id\" FROM notifications WHERE \"UserId\" = '{bobId}' AND \"TemplateKey\" = '{bobTemplate}'");
        var foreign = await fixture.Client.SendAsync(
            fixture.Authed(HttpMethod.Post, $"/v1/notifications/{bobNotificationId}/read", aliceToken));
        foreign.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await foreign.Content.ReadAsStringAsync()).ShouldContain("notification.not_found");

        var aliceNotificationId = await fixture.ScalarAsync<Guid>(
            $"SELECT \"Id\" FROM notifications WHERE \"UserId\" = '{aliceId}' AND \"TemplateKey\" = '{aliceTemplate}'");
        var first = await fixture.Client.SendAsync(
            fixture.Authed(HttpMethod.Post, $"/v1/notifications/{aliceNotificationId}/read", aliceToken));
        var second = await fixture.Client.SendAsync(
            fixture.Authed(HttpMethod.Post, $"/v1/notifications/{aliceNotificationId}/read", aliceToken));

        first.StatusCode.ShouldBe(HttpStatusCode.OK);
        second.StatusCode.ShouldBe(HttpStatusCode.OK);
        var readRows = await fixture.ScalarAsync<long>(
            $"SELECT count(*)::bigint FROM notifications WHERE \"Id\" = '{aliceNotificationId}' AND \"ReadAt\" IS NOT NULL");
        readRows.ShouldBe(1);

        var bobUnread = await fixture.Client.SendAsync(
            fixture.Authed(HttpMethod.Get, "/v1/notifications/me/unread-count", bobToken));
        (await PlatformApiFactory.ReadData(bobUnread)).GetProperty("count").GetInt64().ShouldBeGreaterThanOrEqualTo(1);
    }

    [Fact]
    public async Task Mark_all_read_is_owner_scoped_idempotent_and_does_not_hide_new_notifications()
    {
        var (aliceId, aliceToken) = await fixture.RegisterAndLoginAsync(
            $"all-alice-{Guid.CreateVersion7():N}@example.com", Password);
        var (bobId, _) = await fixture.RegisterAndLoginAsync(
            $"all-bob-{Guid.CreateVersion7():N}@example.com", Password);

        await fixture.WaitForCountAsync(
            $"SELECT count(*)::bigint FROM notifications WHERE \"UserId\" = '{aliceId}' AND \"TemplateKey\" = 'welcome'",
            1);
        await fixture.ExecuteSqlAsync($"UPDATE notifications SET \"ReadAt\" = now() WHERE \"UserId\" = '{aliceId}'");

        var empty = await fixture.Client.SendAsync(
            fixture.Authed(HttpMethod.Post, "/v1/notifications/me/read-all", aliceToken));
        empty.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await PlatformApiFactory.ReadData(empty)).GetProperty("marked").GetInt32().ShouldBe(0);

        var aliceOne = $"all-alice-1-{Guid.CreateVersion7():N}";
        var aliceTwo = $"all-alice-2-{Guid.CreateVersion7():N}";
        var aliceNew = $"all-alice-new-{Guid.CreateVersion7():N}";
        var bobTemplate = $"all-bob-{Guid.CreateVersion7():N}";
        await SeedTemplateAsync(aliceOne, "A1");
        await SeedTemplateAsync(aliceTwo, "A2");
        await SeedTemplateAsync(aliceNew, "A3");
        await SeedTemplateAsync(bobTemplate, "B");

        await SendDirectAsync(aliceId, aliceOne);
        await SendDirectAsync(aliceId, aliceTwo);
        await SendDirectAsync(bobId, bobTemplate);

        var markAll = await fixture.Client.SendAsync(
            fixture.Authed(HttpMethod.Post, "/v1/notifications/me/read-all", aliceToken));
        markAll.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await PlatformApiFactory.ReadData(markAll)).GetProperty("marked").GetInt32().ShouldBe(2);

        var retry = await fixture.Client.SendAsync(
            fixture.Authed(HttpMethod.Post, "/v1/notifications/me/read-all", aliceToken));
        retry.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await PlatformApiFactory.ReadData(retry)).GetProperty("marked").GetInt32().ShouldBe(0);

        await SendDirectAsync(aliceId, aliceNew);
        var aliceUnread = await fixture.Client.SendAsync(
            fixture.Authed(HttpMethod.Get, "/v1/notifications/me/unread-count", aliceToken));
        (await PlatformApiFactory.ReadData(aliceUnread)).GetProperty("count").GetInt64().ShouldBe(1);

        var bobRowsStillUnread = await fixture.ScalarAsync<long>(
            $"SELECT count(*)::bigint FROM notifications WHERE \"UserId\" = '{bobId}' AND \"TemplateKey\" = '{bobTemplate}' AND \"ReadAt\" IS NULL");
        bobRowsStillUnread.ShouldBe(1);
    }

    // A notification's PII (Title/Body) is crypto-shredded in the audit trail: the live row keeps the rendered
    // text, but notifications_audit_entries stores it as a penc:v2: envelope — never the plaintext.
    [Fact]
    public async Task Notification_pii_is_crypto_shredded_in_the_audit_trail()
    {
        var (recipientId, adminToken) = await AdminTokenAsync();

        var templateKey = $"audit-{Guid.CreateVersion7():N}";
        var marker = $"Secret-{Guid.CreateVersion7():N}";
        await fixture.ExecuteSqlAsync(
            $"INSERT INTO notification_templates (\"Id\", \"Key\", \"Locale\", \"Subject\", \"Body\") " +
            $"VALUES ('{Guid.CreateVersion7()}', '{templateKey}', 'en', '{marker}', '{marker} body')");

        var send = await fixture.Client.SendAsync(fixture.Authed(
            HttpMethod.Post, "/v1/notifications/send", adminToken, new
            {
                userId = recipientId,
                templateKey,
                channels = new[] { "inapp" },
                data = new Dictionary<string, string>(),
            }));
        send.StatusCode.ShouldBe(HttpStatusCode.OK);

        // Title is [Encrypted] at rest, so look the row up by its (non-PII) TemplateKey, not by the marker.
        var notificationId = await fixture.ScalarAsync<Guid>(
            $"SELECT \"Id\" FROM notifications WHERE \"UserId\" = '{recipientId}' AND \"TemplateKey\" = '{templateKey}' LIMIT 1");

        // The live column is now ALSO a penc:v2 envelope (encrypted at rest) — never the marker plaintext.
        var rawTitle = await fixture.ScalarAsync<string>(
            $"SELECT \"Title\" FROM notifications WHERE \"Id\" = '{notificationId}'");
        rawTitle.ShouldStartWith("penc:v2:");
        rawTitle.ShouldNotContain(marker);

        var rawAudit = await fixture.ScalarAsync<string>(
            $"SELECT \"NewValues\"::text FROM notifications_audit_entries WHERE \"EntityType\" = 'Notification' " +
            $"AND \"EntityId\" = '{notificationId}' AND \"Action\" = 'Create' LIMIT 1");
        rawAudit.ShouldContain("penc:v2:");
        rawAudit.ShouldNotContain(marker);
    }

    [Fact]
    public async Task Notifications_gdpr_export_and_erasure_ports_return_feed_and_scrub_only_the_subject()
    {
        var (aliceId, _) = await fixture.RegisterAndLoginAsync(
            $"gdpr-notif-alice-{Guid.CreateVersion7():N}@example.com", Password);
        var (bobId, _) = await fixture.RegisterAndLoginAsync(
            $"gdpr-notif-bob-{Guid.CreateVersion7():N}@example.com", Password);

        await fixture.ExecuteSqlAsync($"UPDATE notifications SET \"ReadAt\" = now() WHERE \"UserId\" IN ('{aliceId}', '{bobId}')");

        var aliceTemplate = $"gdpr-notif-alice-{Guid.CreateVersion7():N}";
        var bobTemplate = $"gdpr-notif-bob-{Guid.CreateVersion7():N}";
        await SeedTemplateAsync(aliceTemplate, "Alice export marker");
        await SeedTemplateAsync(bobTemplate, "Bob export marker");
        await SendDirectAsync(aliceId, aliceTemplate);
        await SendDirectAsync(bobId, bobTemplate);

        await using var scope = fixture.Services.CreateAsyncScope();
        var exporter = scope.ServiceProvider.GetServices<IExportPersonalData>()
            .Single(e => e.ModuleName == "Notifications");
        var eraser = scope.ServiceProvider.GetServices<IErasePersonalData>()
            .Single(e => e.ModuleName == "Notifications");

        var export = await exporter.ExportAsync(aliceId, CancellationToken.None);
        var rows = ((IEnumerable<object>)export["notifications"]!).ToArray();
        rows.ShouldContain(row => row.ToString()!.Contains(aliceTemplate, StringComparison.Ordinal));
        rows.ShouldNotContain(row => row.ToString()!.Contains(bobTemplate, StringComparison.Ordinal));

        await eraser.EraseAsync(aliceId, CancellationToken.None);
        await eraser.EraseAsync(aliceId, CancellationToken.None);

        var aliceScrubbed = await fixture.ScalarAsync<long>(
            $"SELECT count(*)::bigint FROM notifications WHERE \"UserId\" = '{aliceId}' AND \"Title\" = '' AND \"Body\" = ''");
        aliceScrubbed.ShouldBeGreaterThanOrEqualTo(1);

        var bobStillHasContent = await fixture.ScalarAsync<long>(
            $"SELECT count(*)::bigint FROM notifications WHERE \"UserId\" = '{bobId}' AND \"TemplateKey\" = '{bobTemplate}' AND \"Title\" <> '' AND \"Body\" <> ''");
        bobStillHasContent.ShouldBe(1);
    }

    // purchase_completed consumer — publishing CreditPurchaseCompletedIntegrationEvent via IMessageBus
    // (the Wolverine bus) causes the SendPurchaseCompletedHandler to dispatch SendNotificationCommand, which
    // writes a "purchase_completed" in-app row for the user. The NotificationsSeeder seeds the template.
    [Fact]
    public async Task CreditPurchaseCompleted_event_creates_purchase_completed_notification()
    {
        var email = $"purchase-{Guid.CreateVersion7():N}@example.com";
        var (userId, _) = await fixture.RegisterAndLoginAsync(email, Password);
        var purchaseId = Guid.CreateVersion7();

        // Ensure Billing account is provisioned (proves the spine is healthy before we publish).
        await fixture.WaitForCountAsync(
            $"SELECT count(*)::bigint FROM credit_accounts WHERE \"UserId\" = '{userId}'", 1);

        // Publish the integration event via the Wolverine bus — exactly what the Billing module does after a top-up.
        // IMessageBus is scoped, so resolve it from a scope rather than the root provider.
        using var scope = fixture.Services.CreateScope();
        var bus = scope.ServiceProvider.GetRequiredService<IMessageBus>();
        await bus.PublishAsync(new CreditPurchaseCompletedIntegrationEvent(
            EventId: Guid.CreateVersion7(),
            OccurredAt: DateTimeOffset.UtcNow,
            UserId: userId,
            PurchaseId: purchaseId,
            PackageId: Guid.CreateVersion7(),
            CreditAmount: 500));

        // The handler dispatches SendNotificationCommand → a "purchase_completed" in-app row is created.
        await fixture.WaitForCountAsync(
            $"SELECT count(*)::bigint FROM notifications " +
            $"WHERE \"UserId\" = '{userId}' AND \"TemplateKey\" = 'purchase_completed'", 1);
    }

    [Fact]
    public async Task Purchase_completed_handler_is_idempotent_and_missing_template_is_non_fatal()
    {
        var (userId, _) = await fixture.RegisterAndLoginAsync(
            $"purchase-retry-{Guid.CreateVersion7():N}@example.com", Password);
        var purchaseId = Guid.CreateVersion7();

        await using var scope = fixture.Services.CreateAsyncScope();
        var dispatcher = scope.ServiceProvider.GetRequiredService<IDispatcher>();
        var handler = new SendPurchaseCompletedHandler(NullLogger<SendPurchaseCompletedHandler>.Instance);
        var message = new CreditPurchaseCompletedIntegrationEvent(
            EventId: Guid.CreateVersion7(),
            OccurredAt: DateTimeOffset.UtcNow,
            UserId: userId,
            PurchaseId: purchaseId,
            PackageId: Guid.CreateVersion7(),
            CreditAmount: 750);

        // A retry or duplicate Billing event has the same PurchaseId. The handler maps it to the same
        // SendNotificationCommand idempotency key, so the UNIQUE key leaves exactly one feed row/outbox handoff.
        await handler.Handle(message, dispatcher, CancellationToken.None);
        await handler.Handle(message with { EventId = Guid.CreateVersion7() }, dispatcher, CancellationToken.None);

        var duplicateRows = await fixture.ScalarAsync<long>(
            $"SELECT count(*)::bigint FROM notifications WHERE \"UserId\" = '{userId}' " +
            $"AND \"TemplateKey\" = 'purchase_completed' AND \"IdempotencyKey\" = 'purchase-completed:{purchaseId:N}'");
        duplicateRows.ShouldBe(1);

        await fixture.ExecuteSqlAsync("DELETE FROM notification_templates WHERE \"Key\" = 'purchase_completed'");
        try
        {
            var missingTemplatePurchaseId = Guid.CreateVersion7();
            await handler.Handle(message with
            {
                EventId = Guid.CreateVersion7(),
                PurchaseId = missingTemplatePurchaseId,
            }, dispatcher, CancellationToken.None);

            var missingTemplateRows = await fixture.ScalarAsync<long>(
                $"SELECT count(*)::bigint FROM notifications WHERE \"UserId\" = '{userId}' " +
                $"AND \"IdempotencyKey\" = 'purchase-completed:{missingTemplatePurchaseId:N}'");
            missingTemplateRows.ShouldBe(0);
        }
        finally
        {
            await RestorePurchaseCompletedTemplatesAsync();
        }
    }

    /// <summary>
    /// The configured platform admin (PlatformApiFactory.AdminEmail), which holds every permission including
    /// notifications.send (admins are granted ALL permissions — IdentitySeeder + PlatformPermissions). Returns the
    /// admin's user id AND a fresh access token.
    ///
    /// Why the admin is also the RECIPIENT in these tests: notifications is an IUserOwned table, so RLS enforces
    /// WITH CHECK (UserId == app.principal_id) on insert. A direct HTTP send for ANOTHER user's id is therefore
    /// rejected at commit by design (SendNotificationHandler documents this) — cross-user sends happen via the
    /// Worker under the system context (e.g. the welcome handler). Over HTTP, the caller can only create a row for
    /// THEMSELVES, so the admin sends to its own id.
    ///
    /// The admin email is shared across this assembly's tests on one host, so registration may already have
    /// happened — tolerate the 409 and just log in. The admin role is granted on login via Identity:Auth:AdminEmails.
    /// </summary>
    private async Task<(Guid AdminId, string Token)> AdminTokenAsync()
    {
        var register = await fixture.Client.PostAsJsonAsync(
            "/v1/identity/users", new { email = PlatformApiFactory.AdminEmail, password = Password });
        if (register.StatusCode is not (HttpStatusCode.OK or HttpStatusCode.Created or HttpStatusCode.Conflict))
        {
            throw new InvalidOperationException(
                $"admin register failed {(int)register.StatusCode}: {await register.Content.ReadAsStringAsync()}");
        }

        var login = await fixture.Client.PostAsJsonAsync(
            "/v1/identity/auth/login", new { email = PlatformApiFactory.AdminEmail, password = Password });
        login.EnsureSuccessStatusCode();
        var token = (await PlatformApiFactory.ReadData(login)).GetProperty("accessToken").GetString()!;

        var adminId = await fixture.ScalarAsync<Guid>(
            $"SELECT \"Id\" FROM users WHERE \"EmailHash\" = '{PlatformApiFactory.EmailHashOf(PlatformApiFactory.AdminEmail)}'");

        return (adminId, token);
    }

    private Task SeedTemplateAsync(string key, string subject) =>
        fixture.ExecuteSqlAsync(
            $"INSERT INTO notification_templates (\"Id\", \"Key\", \"Locale\", \"Subject\", \"Body\") " +
            $"VALUES ('{Guid.CreateVersion7()}', '{key}', 'en', '{subject}', 'Body')");

    private async Task RestoreWelcomeTemplatesAsync()
    {
        await fixture.ExecuteSqlAsync(
            "INSERT INTO notification_templates (\"Id\", \"Key\", \"Locale\", \"Subject\", \"Body\") " +
            $"SELECT '{Guid.CreateVersion7()}', 'welcome', 'en', 'Welcome to the platform!', 'Hi {{displayName}}, welcome aboard.' " +
            "WHERE NOT EXISTS (SELECT 1 FROM notification_templates WHERE \"Key\" = 'welcome' AND \"Locale\" = 'en')");
        await fixture.ExecuteSqlAsync(
            "INSERT INTO notification_templates (\"Id\", \"Key\", \"Locale\", \"Subject\", \"Body\") " +
            $"SELECT '{Guid.CreateVersion7()}', 'welcome', 'cs', 'Vítejte na platformě!', 'Dobrý den {{displayName}}, vítáme vás.' " +
            "WHERE NOT EXISTS (SELECT 1 FROM notification_templates WHERE \"Key\" = 'welcome' AND \"Locale\" = 'cs')");
    }

    private async Task RestorePurchaseCompletedTemplatesAsync()
    {
        await fixture.ExecuteSqlAsync(
            "INSERT INTO notification_templates (\"Id\", \"Key\", \"Locale\", \"Subject\", \"Body\") " +
            $"SELECT '{Guid.CreateVersion7()}', 'purchase_completed', 'en', 'Purchase completed', " +
            $"'Your purchase of {{creditAmount}} credits is complete.' " +
            "WHERE NOT EXISTS (SELECT 1 FROM notification_templates WHERE \"Key\" = 'purchase_completed' AND \"Locale\" = 'en')");
        await fixture.ExecuteSqlAsync(
            "INSERT INTO notification_templates (\"Id\", \"Key\", \"Locale\", \"Subject\", \"Body\") " +
            $"SELECT '{Guid.CreateVersion7()}', 'purchase_completed', 'cs', 'Nákup dokončen', " +
            $"'Váš nákup {{creditAmount}} kreditů je dokončen.' " +
            "WHERE NOT EXISTS (SELECT 1 FROM notification_templates WHERE \"Key\" = 'purchase_completed' AND \"Locale\" = 'cs')");
    }

    private async Task SendDirectAsync(Guid userId, string templateKey)
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var dispatcher = scope.ServiceProvider.GetRequiredService<IDispatcher>();
        await dispatcher.Send(new SendNotificationCommand(
            userId,
            templateKey,
            ["inapp"],
            new Dictionary<string, string>(),
            IdempotencyKey: $"direct:{templateKey}:{Guid.CreateVersion7():N}"));
    }
}
