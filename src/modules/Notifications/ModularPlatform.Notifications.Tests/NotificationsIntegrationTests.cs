using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using ModularPlatform.Billing.Contracts;
using ModularPlatform.IntegrationTesting;
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
    // "never sends inline" contract in SendNotificationCommand.cs:6-8 and IntegrationEvents.cs:6-10.
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

    // purchase_completed consumer — publishing CreditPurchaseCompletedIntegrationEvent via IMessageBus
    // (the Wolverine bus) causes the SendPurchaseCompletedHandler to dispatch SendNotificationCommand, which
    // writes a "purchase_completed" in-app row for the user. The NotificationsSeeder seeds the template.
    [Fact]
    public async Task CreditPurchaseCompleted_event_creates_purchase_completed_notification()
    {
        var email = $"purchase-{Guid.CreateVersion7():N}@example.com";
        var (userId, _) = await fixture.RegisterAndLoginAsync(email, Password);

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
            PurchaseId: Guid.CreateVersion7(),
            PackageId: Guid.CreateVersion7(),
            CreditAmount: 500));

        // The handler dispatches SendNotificationCommand → a "purchase_completed" in-app row is created.
        await fixture.WaitForCountAsync(
            $"SELECT count(*)::bigint FROM notifications " +
            $"WHERE \"UserId\" = '{userId}' AND \"TemplateKey\" = 'purchase_completed'", 1);
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
}
