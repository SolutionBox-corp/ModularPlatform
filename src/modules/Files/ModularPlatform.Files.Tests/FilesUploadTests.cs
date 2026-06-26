using System.Net;
using System.Net.Http.Headers;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using ModularPlatform.Cqrs;
using ModularPlatform.Files.Features.Download;
using ModularPlatform.IntegrationTesting;
using Shouldly;

namespace ModularPlatform.Files.Tests;

/// <summary>
/// The upload→download→list lifecycle end-to-end against the local storage provider: a round-trip returns the same
/// bytes + content-type; the list is paged and owner-scoped; a DIFFERENT user gets 404 on download (RLS); and the
/// content-type allowlist + size cap reject bad uploads.
/// </summary>
[Collection("Integration")]
public sealed class FilesUploadTests(PlatformApiFactory fixture)
{
    private const string Boundary = "----files-test-boundary";

    [Fact]
    public async Task Upload_then_download_round_trips_the_same_bytes_and_content_type()
    {
        var (_, token) = await fixture.RegisterAndLoginAsync($"file-{Guid.CreateVersion7():N}@x.com", "Sup3rSecret!");
        var bytes = Encoding.UTF8.GetBytes("hello modular platform");

        var upload = await UploadAsync(token, "notes.txt", "text/plain", bytes);
        upload.StatusCode.ShouldBe(HttpStatusCode.Created);
        var data = await PlatformApiFactory.ReadData(upload);
        var fileId = data.GetProperty("id").GetGuid();
        data.GetProperty("fileName").GetString().ShouldBe("notes.txt");
        data.GetProperty("contentType").GetString().ShouldBe("text/plain");
        data.GetProperty("size").GetInt64().ShouldBe(bytes.LongLength);

        var download = await fixture.Client.SendAsync(
            fixture.Authed(HttpMethod.Get, $"/v1/files/{fileId}", token));
        download.StatusCode.ShouldBe(HttpStatusCode.OK);
        download.Content.Headers.ContentType!.MediaType.ShouldBe("text/plain");
        (await download.Content.ReadAsByteArrayAsync()).ShouldBe(bytes);
    }

    [Fact]
    public async Task List_is_paged_and_owner_scoped()
    {
        var (_, token) = await fixture.RegisterAndLoginAsync($"list-{Guid.CreateVersion7():N}@x.com", "Sup3rSecret!");

        for (var i = 0; i < 3; i++)
        {
            var up = await UploadAsync(token, $"f{i}.txt", "text/plain", Encoding.UTF8.GetBytes($"body-{i}"));
            up.StatusCode.ShouldBe(HttpStatusCode.Created);
        }

        var list = await fixture.Client.SendAsync(
            fixture.Authed(HttpMethod.Get, "/v1/files?page=1&pageSize=2", token));
        list.StatusCode.ShouldBe(HttpStatusCode.OK);
        var data = await PlatformApiFactory.ReadData(list);
        data.GetProperty("pageSize").GetInt32().ShouldBe(2);
        data.GetProperty("totalCount").GetInt64().ShouldBe(3);
        data.GetProperty("items").GetArrayLength().ShouldBe(2);

        // A different user sees an empty list — RLS owner-scoping.
        var (_, otherToken) = await fixture.RegisterAndLoginAsync($"empty-{Guid.CreateVersion7():N}@x.com", "Sup3rSecret!");
        var otherList = await fixture.Client.SendAsync(
            fixture.Authed(HttpMethod.Get, "/v1/files", otherToken));
        var otherData = await PlatformApiFactory.ReadData(otherList);
        otherData.GetProperty("totalCount").GetInt64().ShouldBe(0);
    }

    [Fact]
    public async Task List_search_filters_by_filename_and_deleted_files_disappear()
    {
        var (_, token) = await fixture.RegisterAndLoginAsync(
            $"list-search-{Guid.CreateVersion7():N}@x.com", "Sup3rSecret!");

        var contract = await UploadAsync(
            token, "Q4-CONTRACT.txt", "text/plain", Encoding.UTF8.GetBytes("contract"));
        var invoice = await UploadAsync(
            token, "invoice.txt", "text/plain", Encoding.UTF8.GetBytes("invoice"));
        await UploadAsync(token, "notes.txt", "text/plain", Encoding.UTF8.GetBytes("notes"));
        contract.StatusCode.ShouldBe(HttpStatusCode.Created);
        invoice.StatusCode.ShouldBe(HttpStatusCode.Created);
        var contractId = (await PlatformApiFactory.ReadData(contract)).GetProperty("id").GetGuid();

        var search = await fixture.Client.SendAsync(
            fixture.Authed(HttpMethod.Get, "/v1/files?search=contract", token));
        search.StatusCode.ShouldBe(HttpStatusCode.OK);
        var searchData = await PlatformApiFactory.ReadData(search);
        searchData.GetProperty("totalCount").GetInt64().ShouldBe(1);
        searchData.GetProperty("items")[0].GetProperty("fileName").GetString().ShouldBe("Q4-CONTRACT.txt");

        var delete = await fixture.Client.SendAsync(
            fixture.Authed(HttpMethod.Delete, $"/v1/files/{contractId}", token));
        delete.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var afterDelete = await fixture.Client.SendAsync(
            fixture.Authed(HttpMethod.Get, "/v1/files?search=contract", token));
        afterDelete.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await PlatformApiFactory.ReadData(afterDelete)).GetProperty("totalCount").GetInt64().ShouldBe(0);
    }

    [Fact]
    public async Task A_different_user_cannot_download_another_users_file()
    {
        var (_, owner) = await fixture.RegisterAndLoginAsync($"owner-{Guid.CreateVersion7():N}@x.com", "Sup3rSecret!");
        var upload = await UploadAsync(owner, "secret.txt", "text/plain", Encoding.UTF8.GetBytes("top secret"));
        var fileId = (await PlatformApiFactory.ReadData(upload)).GetProperty("id").GetGuid();

        var (_, intruder) = await fixture.RegisterAndLoginAsync($"intruder-{Guid.CreateVersion7():N}@x.com", "Sup3rSecret!");
        var foreign = await fixture.Client.SendAsync(
            fixture.Authed(HttpMethod.Get, $"/v1/files/{fileId}", intruder));
        foreign.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Upload_location_header_is_versioned_and_points_at_the_download_route()
    {
        var (_, token) = await fixture.RegisterAndLoginAsync($"loc-{Guid.CreateVersion7():N}@x.com", "Sup3rSecret!");
        var upload = await UploadAsync(token, "notes.txt", "text/plain", Encoding.UTF8.GetBytes("body"));
        upload.StatusCode.ShouldBe(HttpStatusCode.Created);
        var fileId = (await PlatformApiFactory.ReadData(upload)).GetProperty("id").GetGuid();

        // The Location must carry the host's /v1 group prefix and resolve to the real download route — not a
        // string-concatenated path that 404s.
        upload.Headers.Location!.ToString().ShouldBe($"/v1/files/{fileId}");
    }

    [Fact]
    public async Task Upload_uses_server_generated_storage_key_not_the_client_filename()
    {
        var (userId, token) = await fixture.RegisterAndLoginAsync(
            $"key-{Guid.CreateVersion7():N}@x.com", "Sup3rSecret!");

        var upload = await UploadAsync(
            token,
            "../crm/contracts/q4.txt",
            "text/plain",
            Encoding.UTF8.GetBytes("contract body"));
        upload.StatusCode.ShouldBe(HttpStatusCode.Created);
        var fileId = (await PlatformApiFactory.ReadData(upload)).GetProperty("id").GetGuid();

        var storageKey = await fixture.ScalarAsync<string>(
            $"SELECT \"StorageKey\" FROM file_objects WHERE \"Id\" = '{fileId}'");

        storageKey.ShouldBe($"{userId:N}/{fileId:N}");
        storageKey.ShouldNotContain("..");
        storageKey.ShouldNotContain("q4.txt");
    }

    [Fact]
    public async Task Gdpr_erasure_deletes_the_users_files_and_metadata()
    {
        var (userId, token) = await fixture.RegisterAndLoginAsync($"gdpr-{Guid.CreateVersion7():N}@x.com", "Sup3rSecret!");
        var upload = await UploadAsync(token, "cv.pdf", "application/pdf", Encoding.UTF8.GetBytes("resume bytes"));
        upload.StatusCode.ShouldBe(HttpStatusCode.Created);

        await fixture.WaitForCountAsync(
            $"SELECT count(*)::bigint FROM file_objects WHERE \"UserId\" = '{userId}'", 1);

        var erase = await fixture.Client.SendAsync(fixture.Authed(HttpMethod.Post, "/v1/gdpr/me/erase", token));
        erase.EnsureSuccessStatusCode();

        // Async erasure — poll until the subject key is shredded (the erasers run BEFORE the shred), then the
        // user's files must be gone (rows AND blobs).
        await fixture.WaitForCountAsync(
            $"""SELECT count(*)::bigint FROM subject_keys WHERE "UserId" = '{userId}' AND "DeletedAt" IS NOT NULL""", 1);

        (await fixture.ScalarAsync<long>(
            $"SELECT count(*)::bigint FROM file_objects WHERE \"UserId\" = '{userId}'"))
            .ShouldBe(0, "GDPR erasure must delete the user's file metadata");
    }

    [Fact]
    public async Task File_download_is_owner_scoped_at_the_app_layer_even_when_rls_is_bypassed()
    {
        var (_, owner) = await fixture.RegisterAndLoginAsync($"appf-{Guid.CreateVersion7():N}@x.com", "Sup3rSecret!");
        var upload = await UploadAsync(owner, "secret.txt", "text/plain", Encoding.UTF8.GetBytes("top secret"));
        var fileId = (await PlatformApiFactory.ReadData(upload)).GetProperty("id").GetGuid();

        var intruderId = Guid.CreateVersion7();

        // Dispatch the read query in-process under the SYSTEM context (no HttpContext → RLS is bypassed, exactly as
        // the Worker runs). With RLS no longer the gate, ONLY the app-level owner filter can stop the leak — this
        // proves the download is safe even in a deployment that runs with Persistence:Rls:Enabled=false.
        await using var scope = fixture.Services.CreateAsyncScope();
        var dispatcher = scope.ServiceProvider.GetRequiredService<IDispatcher>();

        await Should.ThrowAsync<NotFoundException>(
            () => dispatcher.Query(new GetFileQuery(fileId, intruderId)));
    }

    [Fact]
    public async Task Disallowed_content_type_is_rejected()
    {
        var (userId, token) = await fixture.RegisterAndLoginAsync($"bad-{Guid.CreateVersion7():N}@x.com", "Sup3rSecret!");

        var upload = await UploadAsync(token, "evil.exe", "application/x-msdownload", Encoding.UTF8.GetBytes("MZ"));

        upload.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await upload.Content.ReadAsStringAsync()).ShouldContain("file.content_type.not_allowed");
        // ...and nothing was persisted (no metadata row for the rejected upload).
        (await fixture.ScalarAsync<long>(
            $"SELECT count(*)::bigint FROM file_objects WHERE \"UserId\" = '{userId}'")).ShouldBe(0);
    }

    [Fact]
    public async Task Oversized_file_is_rejected()
    {
        var (userId, token) = await fixture.RegisterAndLoginAsync($"big-{Guid.CreateVersion7():N}@x.com", "Sup3rSecret!");

        // 11 MB > the 10 MB cap.
        var bytes = new byte[11 * 1024 * 1024];
        var upload = await UploadAsync(token, "big.bin", "application/pdf", bytes);

        // Rejected either by the validator (400) or the request-body size limit (413).
        ((int)upload.StatusCode).ShouldBeOneOf(StatusCodes.BadRequest, StatusCodes.PayloadTooLarge);
        // ...and nothing was persisted.
        (await fixture.ScalarAsync<long>(
            $"SELECT count(*)::bigint FROM file_objects WHERE \"UserId\" = '{userId}'")).ShouldBe(0);
    }

    private async Task<HttpResponseMessage> UploadAsync(string token, string fileName, string contentType, byte[] bytes)
    {
        var content = new MultipartFormDataContent(Boundary);
        var filePart = new ByteArrayContent(bytes);
        filePart.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        content.Add(filePart, "file", fileName);

        var request = new HttpRequestMessage(HttpMethod.Post, "/v1/files")
        {
            Content = content,
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return await fixture.Client.SendAsync(request);
    }

    // A download for a file id that was never uploaded is a clean 404 (file.not_found) — never a 500 from a null
    // descriptor, and storage is never touched with a bogus key (the handler throws before IFileStorage).
    [Fact]
    public async Task Download_of_a_nonexistent_file_id_returns_404_not_500()
    {
        var (_, token) = await fixture.RegisterAndLoginAsync($"missing-{Guid.CreateVersion7():N}@x.com", "Sup3rSecret!");

        var download = await fixture.Client.SendAsync(
            fixture.Authed(HttpMethod.Get, $"/v1/files/{Guid.CreateVersion7()}", token));

        download.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await download.Content.ReadAsStringAsync()).ShouldContain("file.not_found");
    }

    private static class StatusCodes
    {
        public const int BadRequest = 400;
        public const int PayloadTooLarge = 413;
    }
}
