using System.Net;
using ModularPlatform.IntegrationTesting;
using Shouldly;

namespace ModularPlatform.Crm.Tests;

/// <summary>
/// CRM's participation in the platform GDPR fan-out via the real Gdpr HTTP routes (mirrors <c>MarketingGdprTests</c>):
/// the export query fans out <see cref="ModularPlatform.Abstractions.IExportPersonalData"/> into one document keyed by
/// module — a user with CRM data gets a populated <c>Crm</c> section; the erasure pipeline fans out
/// <see cref="ModularPlatform.Abstractions.IErasePersonalData"/> — CRM scrubs the subject's free-text PII to
/// <c>[erased]</c> and soft-deletes (the rows are the user's OWN records, anonymized rather than physically deleted).
/// SoloMode drains the durable erase worker in-process.
/// </summary>
[Collection("Integration")]
public sealed class CrmGdprTests(PlatformApiFactory fixture)
{
    private static string Email() => $"crm-gdpr-{Guid.CreateVersion7():N}@x.com";

    [Fact]
    public async Task Export_includes_the_crm_section_and_erase_scrubs_the_subjects_pii()
    {
        var (userId, token) = await fixture.RegisterAndLoginAsync(Email(), "Sup3rSecret!");

        // Seed CRM data the subject owns: a contact (+ an interaction logging free text) and a task.
        var createContact = await fixture.Client.SendAsync(fixture.Authed(HttpMethod.Post, "/v1/crm/contacts", token,
            new { firstName = "Joe", lastName = "Subject", email = "joe.subject@x.com", notes = "private note" }));
        createContact.StatusCode.ShouldBe(HttpStatusCode.Created, await createContact.Content.ReadAsStringAsync());
        var contactId = (await PlatformApiFactory.ReadData(createContact)).GetProperty("id").GetGuid();

        var addInteraction = await fixture.Client.SendAsync(fixture.Authed(
            HttpMethod.Post, $"/v1/crm/contacts/{contactId}/interactions", token,
            new { type = "note", body = "called the subject" }));
        addInteraction.StatusCode.ShouldBe(HttpStatusCode.Created, await addInteraction.Content.ReadAsStringAsync());

        var createTask = await fixture.Client.SendAsync(fixture.Authed(HttpMethod.Post, "/v1/crm/tasks", token,
            new { title = "Follow up with Joe" }));
        createTask.StatusCode.ShouldBe(HttpStatusCode.Created, await createTask.Content.ReadAsStringAsync());

        // ----- EXPORT: the Crm section is present and carries the subject's rows --------------------------------
        var export = await fixture.Client.SendAsync(fixture.Authed(HttpMethod.Get, "/v1/gdpr/me/export", token));
        export.StatusCode.ShouldBe(HttpStatusCode.OK);

        var document = await PlatformApiFactory.ReadData(export);
        document.TryGetProperty("Crm", out var crm).ShouldBeTrue("the export must carry the Crm section");
        crm.GetProperty("contacts").GetArrayLength().ShouldBeGreaterThanOrEqualTo(1);
        crm.GetProperty("interactions").GetArrayLength().ShouldBeGreaterThanOrEqualTo(1);
        crm.GetProperty("tasks").GetArrayLength().ShouldBeGreaterThanOrEqualTo(1);
        crm.GetProperty("contacts").EnumerateArray()
            .Select(c => c.GetProperty("id").GetGuid())
            .ShouldContain(contactId);

        // ----- ERASE: the subject's free-text PII is scrubbed (durable async fan-out → poll) --------------------
        var erase = await fixture.Client.SendAsync(fixture.Authed(HttpMethod.Post, "/v1/gdpr/me/erase", token));
        erase.StatusCode.ShouldBe(HttpStatusCode.OK);

        // The contact's name is anonymized to "[erased]" and its e-mail/blind-index/notes cleared.
        await fixture.WaitForCountAsync(
            $"""SELECT count(*)::bigint FROM crm_contacts WHERE "UserId" = '{userId}' AND "FirstName" = '[erased]' AND "LastName" = '[erased]'""", 1);
        var leakedEmails = await fixture.ScalarAsync<long>(
            $"""SELECT count(*)::bigint FROM crm_contacts WHERE "UserId" = '{userId}' AND "EmailHash" IS NOT NULL""");
        leakedEmails.ShouldBe(0);
        // The logged interaction body is cleared.
        await fixture.WaitForCountAsync(
            $"""SELECT CASE WHEN count(*) = 0 THEN 1 ELSE 0 END::bigint FROM crm_contact_interactions WHERE "UserId" = '{userId}' AND "Body" IS NOT NULL""", 1);
    }
}
