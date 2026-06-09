using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ModularPlatform.IntegrationTesting;
using Shouldly;

namespace ModularPlatform.Gdpr.Tests;

/// <summary>
/// Integration coverage for the Gdpr module on the shared Testcontainers-Postgres + Api host harness:
/// <list type="bullet">
/// <item>GD-3 — the erasure pipeline end-to-end (HTTP trigger → outbox → Worker fan-out → crypto-shred),</item>
/// <item>GD-4 — the export query fanning out IExportPersonalData into one document keyed by module,</item>
/// <item>GD-5 — the append-only consent grant → withdraw → get round-trip.</item>
/// </list>
/// Routes/tables/columns asserted here are verified against production code (see the agent report).
/// </summary>
[Collection("Integration")]
public sealed class GdprIntegrationTests(PlatformApiFactory fixture)
{
    private const string Password = "Sup3rSecret!";

    // ---------------------------------------------------------------------------------------------------------
    // GD-3 — Erasure pipeline e2e.
    // POST /v1/gdpr/me/erase (RequestErasureEndpoint.cs:15) publishes UserErasureRequested via the outbox
    // (RequestErasureHandler.cs:22); the Worker runs UserErasureRequestedHandler (UserErasureRequestedHandler.cs:32)
    // which (a) calls every IErasePersonalData (Notifications blanks Title/Body — NotificationsPersonalDataEraser.cs:28;
    // Billing is a documented no-op that RETAINS the ledger — BillingPersonalDataEraser.cs:28) and (b) dispatches
    // ShredSubjectKeyCommand (ShredSubjectKeyHandler.cs:24) which drops WrappedDek + stamps DeletedAt.
    // ---------------------------------------------------------------------------------------------------------
    [Fact]
    public async Task Erasure_blanks_notification_pii_shreds_the_subject_key_and_retains_the_billing_ledger()
    {
        var email = $"erase-{Guid.CreateVersion7():N}@example.com";
        var (userId, accessToken) = await fixture.RegisterAndLoginAsync(email, Password);

        // Registration publishes UserRegisteredIntegrationEvent; Billing provisions the credit account async.
        await fixture.WaitForCountAsync(
            $"SELECT count(*)::bigint FROM credit_accounts WHERE \"UserId\" = '{userId}'", 1);

        // Seed a notification row carrying PII in Title/Body (the in-app feed substitutes the subject's data).
        var notificationId = Guid.CreateVersion7();
        await fixture.ExecuteSqlAsync(
            $"""
             INSERT INTO notifications ("Id", "UserId", "TemplateKey", "Channel", "Title", "Body", "CreatedAt")
             VALUES ('{notificationId}', '{userId}', 'welcome', 'inapp',
                     'Hello Jane Doe', 'Your email jane.doe@example.com is confirmed', now())
             """);

        // SubjectKey rows are NOT auto-created by any production slice (ShredSubjectKeyHandler treats a missing
        // key as a no-op), so seed one with a live DEK to prove the shred actually runs.
        var subjectKeyId = Guid.CreateVersion7();
        await fixture.ExecuteSqlAsync(
            $"""
             INSERT INTO subject_keys ("Id", "UserId", "WrappedDek", "CreatedAt", "DeletedAt")
             VALUES ('{subjectKeyId}', '{userId}', '\x0102030405', now(), NULL)
             """);

        // Capture the ledger row count BEFORE erasure — it must be retained unchanged for AML/tax.
        var entriesBefore = await fixture.ScalarAsync<long>(
            $"""
             SELECT count(*)::bigint FROM credit_entries e
             JOIN credit_accounts a ON a."Id" = e."AccountId"
             WHERE a."UserId" = '{userId}'
             """);

        // Trigger erasure through the real HTTP route (subject = the authenticated user, never a body id).
        var erase = await fixture.Client.SendAsync(
            fixture.Authed(HttpMethod.Post, "/v1/gdpr/me/erase", accessToken));
        erase.StatusCode.ShouldBe(HttpStatusCode.OK);

        // The erasure is durable + async — poll until the crypto-shred has stamped DeletedAt.
        await fixture.WaitForCountAsync(
            $"""SELECT count(*)::bigint FROM subject_keys WHERE "Id" = '{subjectKeyId}' AND "DeletedAt" IS NOT NULL""",
            1);

        // SubjectKey: DEK destroyed (WrappedDek null) + DeletedAt stamped.
        var liveKeyBytes = await fixture.ScalarAsync<long>(
            $"""SELECT count(*)::bigint FROM subject_keys WHERE "Id" = '{subjectKeyId}' AND "WrappedDek" IS NOT NULL""");
        liveKeyBytes.ShouldBe(0);

        // Notification PII anonymized in place (rows kept, free-text blanked to empty — both columns are NOT NULL).
        var notifPiiRows = await fixture.ScalarAsync<long>(
            $"""
             SELECT count(*)::bigint FROM notifications
             WHERE "Id" = '{notificationId}' AND ("Title" <> '' OR "Body" <> '')
             """);
        notifPiiRows.ShouldBe(0);

        // The notification row itself survives (feed structural integrity is preserved).
        var notifSurvives = await fixture.ScalarAsync<long>(
            $"""SELECT count(*)::bigint FROM notifications WHERE "Id" = '{notificationId}'""");
        notifSurvives.ShouldBe(1);

        // Billing ledger UNCHANGED — append-only rows retained for AML/tax (erasure is via the shredded key).
        var entriesAfter = await fixture.ScalarAsync<long>(
            $"""
             SELECT count(*)::bigint FROM credit_entries e
             JOIN credit_accounts a ON a."Id" = e."AccountId"
             WHERE a."UserId" = '{userId}'
             """);
        entriesAfter.ShouldBe(entriesBefore);

        // The wallet row itself is retained too (ledger integrity).
        var accountSurvives = await fixture.ScalarAsync<long>(
            $"""SELECT count(*)::bigint FROM credit_accounts WHERE "UserId" = '{userId}'""");
        accountSurvives.ShouldBe(1);
    }

    // ---------------------------------------------------------------------------------------------------------
    // GD-4 — Export fan-out.
    // GET /v1/gdpr/me/export (ExportUserDataEndpoint.cs:16) → ExportUserDataHandler (ExportUserDataHandler.cs:14)
    // assembles ONE document keyed by IExportPersonalData.ModuleName. Implementations:
    //   Identity (IdentityPersonalDataExporter.cs:15), Billing (BillingPersonalDataExporter.cs:15),
    //   Notifications (NotificationsPersonalDataExporter.cs:15). Gdpr does NOT implement the export port.
    // ---------------------------------------------------------------------------------------------------------
    [Fact]
    public async Task Export_assembles_one_document_keyed_by_module_with_each_modules_section()
    {
        var email = $"export-{Guid.CreateVersion7():N}@example.com";
        var (userId, accessToken) = await fixture.RegisterAndLoginAsync(email, Password);

        // Ensure Billing has provisioned (so its section is non-null), then seed a notification to export.
        await fixture.WaitForCountAsync(
            $"SELECT count(*)::bigint FROM credit_accounts WHERE \"UserId\" = '{userId}'", 1);

        var notificationId = Guid.CreateVersion7();
        await fixture.ExecuteSqlAsync(
            $"""
             INSERT INTO notifications ("Id", "UserId", "TemplateKey", "Channel", "Title", "Body", "CreatedAt")
             VALUES ('{notificationId}', '{userId}', 'welcome', 'inapp',
                     'Welcome aboard', 'Glad to have you', now())
             """);

        var response = await fixture.Client.SendAsync(
            fixture.Authed(HttpMethod.Get, "/v1/gdpr/me/export", accessToken));
        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var data = await PlatformApiFactory.ReadData(response);

        // One section per module that implements IExportPersonalData — keyed by ModuleName.
        data.TryGetProperty("Identity", out _).ShouldBeTrue();
        data.TryGetProperty("Billing", out var billing).ShouldBeTrue();
        data.TryGetProperty("Notifications", out var notifications).ShouldBeTrue();

        // Billing section carries the provisioned wallet (account non-null).
        billing.GetProperty("account").ValueKind.ShouldNotBe(JsonValueKind.Null);

        // Notifications section carries the seeded feed row with its content.
        var feed = notifications.GetProperty("notifications");
        feed.GetArrayLength().ShouldBeGreaterThanOrEqualTo(1);
        var titles = feed.EnumerateArray()
            .Select(n => n.GetProperty("title").GetString())
            .ToList();
        titles.ShouldContain("Welcome aboard");
    }

    // ---------------------------------------------------------------------------------------------------------
    // GD-5 — Consent grant → withdraw → get round-trip (append-only).
    // POST /v1/gdpr/consents/grant (GrantConsentEndpoint.cs:14), POST /v1/gdpr/consents/withdraw
    // (WithdrawConsentEndpoint.cs:14), GET /v1/gdpr/me/consents (GetConsentsEndpoint.cs:14). Each grant/withdraw
    // APPENDS a new consent_records row (GrantConsentHandler.cs:17 / WithdrawConsentHandler.cs:17); GetConsents
    // returns the full history newest-first (GetConsentsHandler.cs:19). ConsentResponse = (Id, ConsentType,
    // Granted, RecordedAt) (GetConsentsQuery.cs:7).
    // ---------------------------------------------------------------------------------------------------------
    [Fact]
    public async Task Consent_grant_then_withdraw_is_append_only_and_get_reflects_the_latest_state()
    {
        var email = $"consent-{Guid.CreateVersion7():N}@example.com";
        var (userId, accessToken) = await fixture.RegisterAndLoginAsync(email, Password);

        var consentType = $"marketing-{Guid.CreateVersion7():N}";

        // Grant.
        var grant = await fixture.Client.SendAsync(
            fixture.Authed(HttpMethod.Post, "/v1/gdpr/consents/grant", accessToken,
                new { userId, consentType }));
        grant.StatusCode.ShouldBe(HttpStatusCode.OK);

        // Withdraw the SAME consent type — appends a second row rather than mutating the first.
        var withdraw = await fixture.Client.SendAsync(
            fixture.Authed(HttpMethod.Post, "/v1/gdpr/consents/withdraw", accessToken,
                new { userId, consentType }));
        withdraw.StatusCode.ShouldBe(HttpStatusCode.OK);

        // GET reflects the latest state (Granted = false) for this consent type, newest-first.
        var get = await fixture.Client.SendAsync(
            fixture.Authed(HttpMethod.Get, "/v1/gdpr/me/consents", accessToken));
        get.StatusCode.ShouldBe(HttpStatusCode.OK);

        var data = await PlatformApiFactory.ReadData(get);
        var rowsForType = data.EnumerateArray()
            .Where(c => c.GetProperty("consentType").GetString() == consentType)
            .ToList();

        // Append-only: both transitions are preserved as separate rows.
        rowsForType.Count.ShouldBe(2);

        // Newest-first ordering → the first row for this type is the latest state (withdrawn = false).
        rowsForType[0].GetProperty("granted").GetBoolean().ShouldBeFalse();
        rowsForType.ShouldContain(c => c.GetProperty("granted").GetBoolean());

        // The append-only history is also visible at the table level (defence-in-depth assertion).
        var historyRows = await fixture.ScalarAsync<long>(
            $"""
             SELECT count(*)::bigint FROM consent_records
             WHERE "UserId" = '{userId}' AND "ConsentType" = '{consentType}'
             """);
        historyRows.ShouldBe(2);
    }
}
