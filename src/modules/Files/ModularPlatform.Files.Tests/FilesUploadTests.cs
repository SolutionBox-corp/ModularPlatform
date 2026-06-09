using System.Net;
using System.Net.Http.Headers;
using System.Text;
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
    public async Task Disallowed_content_type_is_rejected()
    {
        var (_, token) = await fixture.RegisterAndLoginAsync($"bad-{Guid.CreateVersion7():N}@x.com", "Sup3rSecret!");

        var upload = await UploadAsync(token, "evil.exe", "application/x-msdownload", Encoding.UTF8.GetBytes("MZ"));

        upload.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Oversized_file_is_rejected()
    {
        var (_, token) = await fixture.RegisterAndLoginAsync($"big-{Guid.CreateVersion7():N}@x.com", "Sup3rSecret!");

        // 11 MB > the 10 MB cap.
        var bytes = new byte[11 * 1024 * 1024];
        var upload = await UploadAsync(token, "big.bin", "application/pdf", bytes);

        // Rejected either by the validator (400) or the request-body size limit (413).
        ((int)upload.StatusCode).ShouldBeOneOf(StatusCodes.BadRequest, StatusCodes.PayloadTooLarge);
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

    private static class StatusCodes
    {
        public const int BadRequest = 400;
        public const int PayloadTooLarge = 413;
    }
}
