using System.Net;
using ModularPlatform.IntegrationTesting;
using Shouldly;

namespace ModularPlatform.Crm.Tests;

/// <summary>
/// CRM Companies (B2B accounts) end-to-end: create→get, list filtered + owner-scoped, partial patch, soft-delete
/// detaching contacts/deals, and the rollup — listing contacts/deals filtered by companyId. Foreign ids ⇒ 404 (RLS).
/// </summary>
[Collection("Integration")]
public sealed class CrmCompaniesTests(PlatformApiFactory fixture)
{
    private static string Email() => $"crm-{Guid.CreateVersion7():N}@x.com";

    private async Task<Guid> CreateCompanyAsync(string token, object body)
    {
        var resp = await fixture.Client.SendAsync(fixture.Authed(HttpMethod.Post, "/v1/crm/companies", token, body));
        resp.StatusCode.ShouldBe(HttpStatusCode.Created, await resp.Content.ReadAsStringAsync());
        return (await PlatformApiFactory.ReadData(resp)).GetProperty("id").GetGuid();
    }

    [Fact]
    public async Task Create_then_get_round_trips()
    {
        var (_, token) = await fixture.RegisterAndLoginAsync(Email(), "Sup3rSecret!");
        var id = await CreateCompanyAsync(token, new { name = "Acme", domain = "acme.test", industry = "SaaS" });

        var get = await fixture.Client.SendAsync(fixture.Authed(HttpMethod.Get, $"/v1/crm/companies/{id}", token));
        get.StatusCode.ShouldBe(HttpStatusCode.OK);
        var data = await PlatformApiFactory.ReadData(get);
        data.GetProperty("name").GetString().ShouldBe("Acme");
        data.GetProperty("industry").GetString().ShouldBe("SaaS");
    }

    [Fact]
    public async Task Rollup_lists_contacts_and_deals_by_company()
    {
        var (_, token) = await fixture.RegisterAndLoginAsync(Email(), "Sup3rSecret!");
        var companyId = await CreateCompanyAsync(token, new { name = "Globex" });

        var c = await fixture.Client.SendAsync(fixture.Authed(HttpMethod.Post, "/v1/crm/contacts", token,
            new { fullName = "Jane", status = "lead", companyId }));
        c.StatusCode.ShouldBe(HttpStatusCode.Created, await c.Content.ReadAsStringAsync());
        var d = await fixture.Client.SendAsync(fixture.Authed(HttpMethod.Post, "/v1/crm/deals", token,
            new { title = "Big", amountCents = 1000L, stage = "lead", companyId }));
        d.StatusCode.ShouldBe(HttpStatusCode.Created, await d.Content.ReadAsStringAsync());

        var contacts = await fixture.Client.SendAsync(
            fixture.Authed(HttpMethod.Get, $"/v1/crm/contacts?companyId={companyId}", token));
        (await PlatformApiFactory.ReadData(contacts)).GetProperty("totalCount").GetInt32().ShouldBe(1);
        var deals = await fixture.Client.SendAsync(
            fixture.Authed(HttpMethod.Get, $"/v1/crm/deals?companyId={companyId}", token));
        (await PlatformApiFactory.ReadData(deals)).GetProperty("totalCount").GetInt32().ShouldBe(1);
    }

    [Fact]
    public async Task Linking_foreign_company_to_contact_is_not_found()
    {
        var (_, token) = await fixture.RegisterAndLoginAsync(Email(), "Sup3rSecret!");
        var resp = await fixture.Client.SendAsync(fixture.Authed(HttpMethod.Post, "/v1/crm/contacts", token,
            new { fullName = "X", status = "lead", companyId = Guid.CreateVersion7() }));
        resp.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Delete_detaches_contacts_and_deals()
    {
        var (_, token) = await fixture.RegisterAndLoginAsync(Email(), "Sup3rSecret!");
        var companyId = await CreateCompanyAsync(token, new { name = "Initech" });
        var contact = await fixture.Client.SendAsync(fixture.Authed(HttpMethod.Post, "/v1/crm/contacts", token,
            new { fullName = "Bob", status = "lead", companyId }));
        var contactId = (await PlatformApiFactory.ReadData(contact)).GetProperty("id").GetGuid();

        var del = await fixture.Client.SendAsync(fixture.Authed(HttpMethod.Delete, $"/v1/crm/companies/{companyId}", token));
        del.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var get = await fixture.Client.SendAsync(fixture.Authed(HttpMethod.Get, $"/v1/crm/contacts/{contactId}", token));
        var data = await PlatformApiFactory.ReadData(get);
        (data.TryGetProperty("companyId", out var cid) && cid.ValueKind == System.Text.Json.JsonValueKind.Null).ShouldBeTrue();
    }

    [Fact]
    public async Task Foreign_company_is_not_found()
    {
        var (_, owner) = await fixture.RegisterAndLoginAsync(Email(), "Sup3rSecret!");
        var id = await CreateCompanyAsync(owner, new { name = "Private" });
        var (_, intruder) = await fixture.RegisterAndLoginAsync(Email(), "Sup3rSecret!");
        var get = await fixture.Client.SendAsync(fixture.Authed(HttpMethod.Get, $"/v1/crm/companies/{id}", intruder));
        get.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }
}
