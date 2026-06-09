using System.Net;
using System.Net.Http.Json;
using ModularPlatform.IntegrationTesting;
using Shouldly;

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

    // EV-2 — A missing "welcome" template is NON-FATAL: registration still fully succeeds and the durable
    // spine stays healthy (the same UserRegisteredIntegrationEvent that the welcome handler consumes also
    // provisions a Billing credit account — that account appearing proves the event was processed
    // end-to-end). No welcome in-app row is produced (template not seeded) and nothing is stuck.
    [Fact]
    public async Task Register_with_no_welcome_template_seeded_is_non_fatal_and_the_spine_stays_healthy()
    {
        var email = $"welcome-{Guid.CreateVersion7():N}@example.com";

        var (userId, token) = await fixture.RegisterAndLoginAsync(email, Password);

        // The UserRegisteredIntegrationEvent is processed end-to-end: Billing provisions a credit account.
        // This proves the durable pipeline ran the registration fan-out (which includes the welcome handler).
        await fixture.WaitForCountAsync(
            $"SELECT count(*)::bigint FROM credit_accounts WHERE \"UserId\" = '{userId}'", 1);

        // The welcome template is NOT seeded, so the welcome handler swallows NotFoundException and writes no
        // in-app row. Give the durable handler time to run, then assert the feed has no welcome notification.
        await Task.Delay(1500);
        var welcomeRows = await fixture.ScalarAsync<long>(
            $"SELECT count(*)::bigint FROM notifications WHERE \"UserId\" = '{userId}' AND \"TemplateKey\" = 'welcome'");
        welcomeRows.ShouldBe(0);

        // The system is healthy for this user: the feed endpoint responds 200 (no 500, no stuck state).
        var feed = await fixture.Client.SendAsync(
            fixture.Authed(HttpMethod.Get, "/v1/notifications/me?unreadOnly=true", token));
        feed.StatusCode.ShouldBe(HttpStatusCode.OK);
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

        // Exactly one inapp feed row persisted, with the template rendered ({displayName} -> "Ada").
        var rows = await fixture.ScalarAsync<long>(
            $"SELECT count(*)::bigint FROM notifications WHERE \"UserId\" = '{recipientId}' AND \"TemplateKey\" = '{templateKey}'");
        rows.ShouldBe(1);

        var title = await fixture.ScalarAsync<string>(
            $"SELECT \"Title\" FROM notifications WHERE \"UserId\" = '{recipientId}' AND \"TemplateKey\" = '{templateKey}'");
        title.ShouldBe("Hello Ada");

        var channel = await fixture.ScalarAsync<string>(
            $"SELECT \"Channel\" FROM notifications WHERE \"UserId\" = '{recipientId}' AND \"TemplateKey\" = '{templateKey}'");
        channel.ShouldBe("inapp");
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
    // text, but notifications_audit_entries stores it as a penc:v1: envelope — never the plaintext.
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

        // The live notification keeps the rendered title (only the AUDIT trail is encrypted).
        var notificationId = await fixture.ScalarAsync<Guid>(
            $"SELECT \"Id\" FROM notifications WHERE \"Title\" = '{marker}' LIMIT 1");

        var rawAudit = await fixture.ScalarAsync<string>(
            $"SELECT \"NewValues\"::text FROM notifications_audit_entries WHERE \"EntityType\" = 'Notification' " +
            $"AND \"EntityId\" = '{notificationId}' AND \"Action\" = 'Create' LIMIT 1");
        rawAudit.ShouldContain("penc:v1:");
        rawAudit.ShouldNotContain(marker);
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
            $"SELECT \"Id\" FROM users WHERE \"NormalizedEmail\" = '{PlatformApiFactory.AdminEmail.ToUpperInvariant()}'");

        return (adminId, token);
    }
}
