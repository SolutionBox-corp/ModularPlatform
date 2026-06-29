using System.Net;
using System.Net.Http.Headers;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using ModularPlatform.Abstractions;
using ModularPlatform.Cqrs;
using ModularPlatform.Files.Entities;
using ModularPlatform.Files.Features.Upload;
using ModularPlatform.Files.Persistence;
using ModularPlatform.Files.Features.Download;
using ModularPlatform.Files.Gdpr;
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
    public async Task List_clamps_page_parameters_and_orders_newest_first_across_pages()
    {
        var (_, token) = await fixture.RegisterAndLoginAsync(
            $"list-order-{Guid.CreateVersion7():N}@x.com", "Sup3rSecret!");

        await UploadAsync(token, "oldest.txt", "text/plain", Encoding.UTF8.GetBytes("oldest"));
        await Task.Delay(20);
        await UploadAsync(token, "middle.txt", "text/plain", Encoding.UTF8.GetBytes("middle"));
        await Task.Delay(20);
        await UploadAsync(token, "newest.txt", "text/plain", Encoding.UTF8.GetBytes("newest"));

        var clamped = await fixture.Client.SendAsync(
            fixture.Authed(HttpMethod.Get, "/v1/files?page=0&pageSize=0", token));
        clamped.StatusCode.ShouldBe(HttpStatusCode.OK);
        var clampedData = await PlatformApiFactory.ReadData(clamped);
        clampedData.GetProperty("page").GetInt32().ShouldBe(1);
        clampedData.GetProperty("pageSize").GetInt32().ShouldBe(1);
        clampedData.GetProperty("totalCount").GetInt64().ShouldBe(3);
        clampedData.GetProperty("items").GetArrayLength().ShouldBe(1);
        clampedData.GetProperty("items")[0].GetProperty("fileName").GetString().ShouldBe("newest.txt");

        var secondPage = await fixture.Client.SendAsync(
            fixture.Authed(HttpMethod.Get, "/v1/files?page=2&pageSize=2", token));
        secondPage.StatusCode.ShouldBe(HttpStatusCode.OK);
        var secondPageData = await PlatformApiFactory.ReadData(secondPage);
        secondPageData.GetProperty("page").GetInt32().ShouldBe(2);
        secondPageData.GetProperty("pageSize").GetInt32().ShouldBe(2);
        secondPageData.GetProperty("items").GetArrayLength().ShouldBe(1);
        secondPageData.GetProperty("items")[0].GetProperty("fileName").GetString().ShouldBe("oldest.txt");
    }

    [Fact]
    public async Task List_orders_created_at_ties_by_id_for_stable_paging()
    {
        var (_, token) = await fixture.RegisterAndLoginAsync(
            $"list-tie-{Guid.CreateVersion7():N}@x.com", "Sup3rSecret!");

        var first = await UploadAsync(token, "first.txt", "text/plain", Encoding.UTF8.GetBytes("first"));
        var second = await UploadAsync(token, "second.txt", "text/plain", Encoding.UTF8.GetBytes("second"));
        first.StatusCode.ShouldBe(HttpStatusCode.Created);
        second.StatusCode.ShouldBe(HttpStatusCode.Created);
        var firstId = (await PlatformApiFactory.ReadData(first)).GetProperty("id").GetGuid();
        var secondId = (await PlatformApiFactory.ReadData(second)).GetProperty("id").GetGuid();

        var sameCreatedAt = "2026-01-01 00:00:00+00";
        await fixture.ExecuteSqlAsync(
            "UPDATE file_objects " +
            $"SET \"CreatedAt\" = timestamp with time zone '{sameCreatedAt}' " +
            $"WHERE \"Id\" IN ('{firstId}', '{secondId}')");

        var list = await fixture.Client.SendAsync(
            fixture.Authed(HttpMethod.Get, "/v1/files?page=1&pageSize=2", token));
        list.StatusCode.ShouldBe(HttpStatusCode.OK);
        var data = await PlatformApiFactory.ReadData(list);

        data.GetProperty("items").GetArrayLength().ShouldBe(2);
        data.GetProperty("items").EnumerateArray()
            .Select(item => item.GetProperty("id").GetGuid())
            .ShouldBe(new[] { firstId, secondId }.OrderByDescending(id => id).ToArray());
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
    public async Task Download_returns_404_when_metadata_exists_but_blob_is_missing()
    {
        var (_, token) = await fixture.RegisterAndLoginAsync(
            $"blob-missing-{Guid.CreateVersion7():N}@x.com", "Sup3rSecret!");
        var upload = await UploadAsync(token, "missing-blob.txt", "text/plain", Encoding.UTF8.GetBytes("body"));
        upload.StatusCode.ShouldBe(HttpStatusCode.Created);
        var fileId = (await PlatformApiFactory.ReadData(upload)).GetProperty("id").GetGuid();
        var storageKey = await fixture.ScalarAsync<string>(
            $"SELECT \"StorageKey\" FROM file_objects WHERE \"Id\" = '{fileId}'");

        await using var scope = fixture.Services.CreateAsyncScope();
        var storage = scope.ServiceProvider.GetRequiredService<IFileStorage>();
        await storage.DeleteAsync(storageKey, CancellationToken.None);

        var download = await fixture.Client.SendAsync(
            fixture.Authed(HttpMethod.Get, $"/v1/files/{fileId}", token));
        download.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await download.Content.ReadAsStringAsync()).ShouldContain("file.not_found");
    }

    [Fact]
    public async Task Rename_updates_display_name_only_and_keeps_storage_key_unchanged()
    {
        var (_, token) = await fixture.RegisterAndLoginAsync(
            $"rename-{Guid.CreateVersion7():N}@x.com", "Sup3rSecret!");
        var upload = await UploadAsync(token, "old-name.txt", "text/plain", Encoding.UTF8.GetBytes("body"));
        upload.StatusCode.ShouldBe(HttpStatusCode.Created);
        var fileId = (await PlatformApiFactory.ReadData(upload)).GetProperty("id").GetGuid();
        var storageKeyBefore = await fixture.ScalarAsync<string>(
            $"SELECT \"StorageKey\" FROM file_objects WHERE \"Id\" = '{fileId}'");

        var rename = await fixture.Client.SendAsync(fixture.Authed(
            HttpMethod.Patch, $"/v1/files/{fileId}", token, new { fileName = "new-name.txt" }));

        rename.StatusCode.ShouldBe(HttpStatusCode.OK);
        var data = await PlatformApiFactory.ReadData(rename);
        data.GetProperty("fileName").GetString().ShouldBe("new-name.txt");

        var row = await fixture.ScalarAsync<string>(
            $"SELECT \"FileName\" FROM file_objects WHERE \"Id\" = '{fileId}'");
        row.ShouldBe("new-name.txt");
        var storageKeyAfter = await fixture.ScalarAsync<string>(
            $"SELECT \"StorageKey\" FROM file_objects WHERE \"Id\" = '{fileId}'");
        storageKeyAfter.ShouldBe(storageKeyBefore);
    }

    [Fact]
    public async Task Rename_validates_file_name_and_keeps_foreign_ids_hidden()
    {
        var (_, ownerToken) = await fixture.RegisterAndLoginAsync(
            $"rename-owner-{Guid.CreateVersion7():N}@x.com", "Sup3rSecret!");
        var upload = await UploadAsync(ownerToken, "owned.txt", "text/plain", Encoding.UTF8.GetBytes("body"));
        upload.StatusCode.ShouldBe(HttpStatusCode.Created);
        var fileId = (await PlatformApiFactory.ReadData(upload)).GetProperty("id").GetGuid();

        var empty = await fixture.Client.SendAsync(fixture.Authed(
            HttpMethod.Patch, $"/v1/files/{fileId}", ownerToken, new { fileName = "" }));
        empty.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await empty.Content.ReadAsStringAsync()).ShouldContain("file.name.required");

        var tooLong = await fixture.Client.SendAsync(fixture.Authed(
            HttpMethod.Patch, $"/v1/files/{fileId}", ownerToken, new { fileName = new string('x', 513) }));
        tooLong.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await tooLong.Content.ReadAsStringAsync()).ShouldContain("file.name.too_long");

        var (_, otherToken) = await fixture.RegisterAndLoginAsync(
            $"rename-other-{Guid.CreateVersion7():N}@x.com", "Sup3rSecret!");
        var foreign = await fixture.Client.SendAsync(fixture.Authed(
            HttpMethod.Patch, $"/v1/files/{fileId}", otherToken, new { fileName = "stolen.txt" }));
        foreign.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Delete_is_owner_scoped_removes_metadata_and_second_delete_is_404()
    {
        var (_, ownerToken) = await fixture.RegisterAndLoginAsync(
            $"delete-owner-{Guid.CreateVersion7():N}@x.com", "Sup3rSecret!");
        var upload = await UploadAsync(ownerToken, "delete-me.txt", "text/plain", Encoding.UTF8.GetBytes("body"));
        upload.StatusCode.ShouldBe(HttpStatusCode.Created);
        var fileId = (await PlatformApiFactory.ReadData(upload)).GetProperty("id").GetGuid();
        var ownerId = Guid.CreateVersion7();
        var link = await fixture.Client.SendAsync(fixture.Authed(
            HttpMethod.Post,
            $"/v1/files/{fileId}/links",
            ownerToken,
            new { ownerType = "files.delete-test", ownerId }));
        link.StatusCode.ShouldBe(HttpStatusCode.Created);

        var (_, otherToken) = await fixture.RegisterAndLoginAsync(
            $"delete-other-{Guid.CreateVersion7():N}@x.com", "Sup3rSecret!");
        var foreign = await fixture.Client.SendAsync(
            fixture.Authed(HttpMethod.Delete, $"/v1/files/{fileId}", otherToken));
        foreign.StatusCode.ShouldBe(HttpStatusCode.NotFound);

        var first = await fixture.Client.SendAsync(
            fixture.Authed(HttpMethod.Delete, $"/v1/files/{fileId}", ownerToken));
        first.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var second = await fixture.Client.SendAsync(
            fixture.Authed(HttpMethod.Delete, $"/v1/files/{fileId}", ownerToken));
        second.StatusCode.ShouldBe(HttpStatusCode.NotFound);

        (await fixture.ScalarAsync<long>(
            $"SELECT count(*)::bigint FROM file_objects WHERE \"Id\" = '{fileId}'")).ShouldBe(0);
        (await fixture.ScalarAsync<long>(
            $"SELECT count(*)::bigint FROM file_links WHERE \"FileObjectId\" = '{fileId}'")).ShouldBe(0);

        var download = await fixture.Client.SendAsync(
            fixture.Authed(HttpMethod.Get, $"/v1/files/{fileId}", ownerToken));
        download.StatusCode.ShouldBe(HttpStatusCode.NotFound);

        var list = await fixture.Client.SendAsync(
            fixture.Authed(HttpMethod.Get, "/v1/files?search=delete-me", ownerToken));
        list.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await PlatformApiFactory.ReadData(list)).GetProperty("totalCount").GetInt64().ShouldBe(0);
    }

    [Fact]
    public async Task File_can_be_linked_listed_idempotently_and_unlinked_without_deleting_the_file()
    {
        var (_, token) = await fixture.RegisterAndLoginAsync($"link-{Guid.CreateVersion7():N}@x.com", "Sup3rSecret!");
        var upload = await UploadAsync(token, "contract.pdf", "application/pdf", Encoding.UTF8.GetBytes("pdf"));
        upload.StatusCode.ShouldBe(HttpStatusCode.Created);
        var fileId = (await PlatformApiFactory.ReadData(upload)).GetProperty("id").GetGuid();
        var ownerId = Guid.CreateVersion7();

        var first = await fixture.Client.SendAsync(fixture.Authed(
            HttpMethod.Post,
            $"/v1/files/{fileId}/links",
            token,
            new { ownerType = "example.record", ownerId }));
        first.StatusCode.ShouldBe(HttpStatusCode.Created);
        var firstData = await PlatformApiFactory.ReadData(first);
        var linkId = firstData.GetProperty("id").GetGuid();
        firstData.GetProperty("fileObjectId").GetGuid().ShouldBe(fileId);
        firstData.GetProperty("ownerType").GetString().ShouldBe("example.record");

        var duplicate = await fixture.Client.SendAsync(fixture.Authed(
            HttpMethod.Post,
            $"/v1/files/{fileId}/links",
            token,
            new { ownerType = "example.record", ownerId }));
        duplicate.StatusCode.ShouldBe(HttpStatusCode.Created);
        (await PlatformApiFactory.ReadData(duplicate)).GetProperty("id").GetGuid().ShouldBe(linkId);
        (await fixture.ScalarAsync<long>(
            $"""SELECT count(*)::bigint FROM file_links WHERE "FileObjectId" = '{fileId}'""")).ShouldBe(1);

        var list = await fixture.Client.SendAsync(fixture.Authed(
            HttpMethod.Get,
            $"/v1/files/links?ownerType=example.record&ownerId={ownerId}",
            token));
        list.StatusCode.ShouldBe(HttpStatusCode.OK, await list.Content.ReadAsStringAsync());
        var listData = await PlatformApiFactory.ReadData(list);
        listData.GetProperty("totalCount").GetInt64().ShouldBe(1);
        listData.GetProperty("items")[0].GetProperty("fileName").GetString().ShouldBe("contract.pdf");

        var unlink = await fixture.Client.SendAsync(fixture.Authed(
            HttpMethod.Delete, $"/v1/files/links/{linkId}", token));
        unlink.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        (await fixture.ScalarAsync<long>(
            $"""SELECT count(*)::bigint FROM file_links WHERE "Id" = '{linkId}'""")).ShouldBe(0);

        var download = await fixture.Client.SendAsync(
            fixture.Authed(HttpMethod.Get, $"/v1/files/{fileId}", token));
        download.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task File_links_are_owner_scoped_and_validate_owner_type()
    {
        var (_, ownerToken) = await fixture.RegisterAndLoginAsync(
            $"link-owner-{Guid.CreateVersion7():N}@x.com", "Sup3rSecret!");
        var upload = await UploadAsync(ownerToken, "owned.txt", "text/plain", Encoding.UTF8.GetBytes("body"));
        upload.StatusCode.ShouldBe(HttpStatusCode.Created);
        var fileId = (await PlatformApiFactory.ReadData(upload)).GetProperty("id").GetGuid();
        var ownerId = Guid.CreateVersion7();

        var invalid = await fixture.Client.SendAsync(fixture.Authed(
            HttpMethod.Post,
            $"/v1/files/{fileId}/links",
            ownerToken,
            new { ownerType = "Example Record", ownerId }));
        invalid.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await invalid.Content.ReadAsStringAsync()).ShouldContain("file.link.owner_type.invalid");

        var (_, otherToken) = await fixture.RegisterAndLoginAsync(
            $"link-other-{Guid.CreateVersion7():N}@x.com", "Sup3rSecret!");
        var foreignFile = await fixture.Client.SendAsync(fixture.Authed(
            HttpMethod.Post,
            $"/v1/files/{fileId}/links",
            otherToken,
            new { ownerType = "example.record", ownerId }));
        foreignFile.StatusCode.ShouldBe(HttpStatusCode.NotFound);

        var create = await fixture.Client.SendAsync(fixture.Authed(
            HttpMethod.Post,
            $"/v1/files/{fileId}/links",
            ownerToken,
            new { ownerType = "example.record", ownerId }));
        create.StatusCode.ShouldBe(HttpStatusCode.Created);
        var linkId = (await PlatformApiFactory.ReadData(create)).GetProperty("id").GetGuid();

        var foreignUnlink = await fixture.Client.SendAsync(
            fixture.Authed(HttpMethod.Delete, $"/v1/files/links/{linkId}", otherToken));
        foreignUnlink.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await foreignUnlink.Content.ReadAsStringAsync()).ShouldContain("file.link_not_found");
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
            "../example/contracts/q4.txt",
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
        var fileId = (await PlatformApiFactory.ReadData(upload)).GetProperty("id").GetGuid();
        var ownerId = Guid.CreateVersion7();

        var link = await fixture.Client.SendAsync(fixture.Authed(
            HttpMethod.Post,
            $"/v1/files/{fileId}/links",
            token,
            new { ownerType = "gdpr.case", ownerId }));
        link.StatusCode.ShouldBe(HttpStatusCode.Created);

        await fixture.WaitForCountAsync(
            $"SELECT count(*)::bigint FROM file_objects WHERE \"UserId\" = '{userId}'", 1);
        await fixture.WaitForCountAsync(
            $"SELECT count(*)::bigint FROM file_links WHERE \"UserId\" = '{userId}'", 1);

        var erase = await fixture.Client.SendAsync(fixture.Authed(HttpMethod.Post, "/v1/gdpr/me/erase", token));
        erase.EnsureSuccessStatusCode();

        // Async erasure — poll until the subject key is shredded (the erasers run BEFORE the shred), then the
        // user's files must be gone (rows AND blobs).
        await fixture.WaitForCountAsync(
            $"""SELECT count(*)::bigint FROM subject_keys WHERE "UserId" = '{userId}' AND "DeletedAt" IS NOT NULL""", 1);

        (await fixture.ScalarAsync<long>(
            $"SELECT count(*)::bigint FROM file_objects WHERE \"UserId\" = '{userId}'"))
            .ShouldBe(0, "GDPR erasure must delete the user's file metadata");
        (await fixture.ScalarAsync<long>(
            $"SELECT count(*)::bigint FROM file_links WHERE \"UserId\" = '{userId}'"))
            .ShouldBe(0, "GDPR erasure must delete the user's file links");
    }

    [Fact]
    public async Task Gdpr_erasure_keeps_metadata_when_blob_delete_fails_so_retry_can_finish()
    {
        var userId = Guid.CreateVersion7();
        var tenant = new TestTenantContext(userId);
        var options = new DbContextOptionsBuilder<FilesDbContext>()
            .UseNpgsql(fixture.ConnectionString)
            .Options;
        var firstKey = $"{userId:N}/{Guid.CreateVersion7():N}";
        var secondKey = $"{userId:N}/{Guid.CreateVersion7():N}";

        await using (var seed = new FilesDbContext(options, tenant))
        {
            seed.Files.AddRange(
                NewFile(userId, firstKey, "first.txt"),
                NewFile(userId, secondKey, "second.txt"));
            await seed.SaveChangesAsync();
        }

        var storage = new FailingOnceFileStorage(failOnKey: secondKey);
        await using (var firstRunDb = new FilesDbContext(options, tenant))
        {
            var eraser = new FilesPersonalDataEraser(firstRunDb, storage);
            await Should.ThrowAsync<InvalidOperationException>(() => eraser.EraseAsync(userId, CancellationToken.None));
        }

        storage.DeleteKeys.ShouldBe([firstKey, secondKey]);
        (await fixture.ScalarAsync<long>(
            $"SELECT count(*)::bigint FROM file_objects WHERE \"UserId\" = '{userId}'"))
            .ShouldBe(2, "metadata must remain until every blob delete succeeds so GDPR fan-out retry can resume");

        await using (var retryDb = new FilesDbContext(options, tenant))
        {
            var eraser = new FilesPersonalDataEraser(retryDb, storage);
            await eraser.EraseAsync(userId, CancellationToken.None);
        }

        storage.DeleteKeys.ShouldBe([firstKey, secondKey, firstKey, secondKey]);
        (await fixture.ScalarAsync<long>(
            $"SELECT count(*)::bigint FROM file_objects WHERE \"UserId\" = '{userId}'")).ShouldBe(0);
    }

    [Fact]
    public async Task Gdpr_export_contains_file_inventory_and_links_without_raw_bytes()
    {
        var (userId, token) = await fixture.RegisterAndLoginAsync(
            $"gdpr-export-files-{Guid.CreateVersion7():N}@x.com", "Sup3rSecret!");

        var bytes = Encoding.UTF8.GetBytes("contract bytes that must not be embedded in export");
        var upload = await UploadAsync(token, "contract.pdf", "application/pdf", bytes);
        upload.StatusCode.ShouldBe(HttpStatusCode.Created);
        var fileId = (await PlatformApiFactory.ReadData(upload)).GetProperty("id").GetGuid();
        var ownerId = Guid.CreateVersion7();

        var link = await fixture.Client.SendAsync(fixture.Authed(
            HttpMethod.Post,
            $"/v1/files/{fileId}/links",
            token,
            new { ownerType = "crm.deal", ownerId }));
        link.StatusCode.ShouldBe(HttpStatusCode.Created);

        await using var scope = fixture.Services.CreateAsyncScope();
        var exporter = scope.ServiceProvider
            .GetServices<IExportPersonalData>()
            .Single(x => x.ModuleName == "Files");

        var export = await exporter.ExportAsync(userId, CancellationToken.None);

        using var filesJson = System.Text.Json.JsonDocument.Parse(System.Text.Json.JsonSerializer.Serialize(export["files"]));
        var files = filesJson.RootElement;
        files.GetArrayLength().ShouldBe(1);
        files[0].GetProperty("Id").GetGuid().ShouldBe(fileId);
        files[0].GetProperty("FileName").GetString().ShouldBe("contract.pdf");
        files[0].GetProperty("ContentType").GetString().ShouldBe("application/pdf");
        files[0].GetProperty("Size").GetInt64().ShouldBe(bytes.LongLength);
        files[0].TryGetProperty("StorageKey", out _).ShouldBeFalse();
        files[0].TryGetProperty("Bytes", out _).ShouldBeFalse();

        using var linksJson = System.Text.Json.JsonDocument.Parse(System.Text.Json.JsonSerializer.Serialize(export["fileLinks"]));
        var links = linksJson.RootElement;
        links.GetArrayLength().ShouldBe(1);
        links[0].GetProperty("FileObjectId").GetGuid().ShouldBe(fileId);
        links[0].GetProperty("OwnerType").GetString().ShouldBe("crm.deal");
        links[0].GetProperty("OwnerId").GetGuid().ShouldBe(ownerId);
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
    public async Task Missing_content_type_is_rejected()
    {
        var (userId, token) = await fixture.RegisterAndLoginAsync(
            $"missing-ct-{Guid.CreateVersion7():N}@x.com", "Sup3rSecret!");

        var upload = await UploadWithoutContentTypeAsync(token, "unknown.bin", Encoding.UTF8.GetBytes("bytes"));

        upload.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await upload.Content.ReadAsStringAsync()).ShouldContain("file.content_type.not_allowed");
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

    [Fact]
    public async Task Upload_cleans_up_blob_when_metadata_persistence_fails()
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<FilesDbContext>();
        var storage = new RecordingFileStorage();
        var handler = new UploadFileHandler(db, storage, NullLogger<UploadFileHandler>.Instance);
        var userId = Guid.CreateVersion7();

        await Should.ThrowAsync<DbUpdateException>(() => handler.Handle(
            new UploadFileCommand(
                userId,
                new MemoryStream(Encoding.UTF8.GetBytes("orphan candidate")),
                new string('x', 600) + ".txt",
                "text/plain",
                16),
            CancellationToken.None));

        storage.PutKeys.Count.ShouldBe(1);
        storage.DeleteKeys.ShouldBe([storage.PutKeys.Single()]);
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

    private async Task<HttpResponseMessage> UploadWithoutContentTypeAsync(string token, string fileName, byte[] bytes)
    {
        var content = new MultipartFormDataContent(Boundary);
        content.Add(new ByteArrayContent(bytes), "file", fileName);

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

    private sealed class RecordingFileStorage : IFileStorage
    {
        public List<string> PutKeys { get; } = [];
        public List<string> DeleteKeys { get; } = [];

        public async Task PutAsync(string key, Stream content, string contentType, CancellationToken ct)
        {
            PutKeys.Add(key);
            await content.CopyToAsync(Stream.Null, ct);
        }

        public Task<Stream> GetAsync(string key, CancellationToken ct) =>
            Task.FromResult<Stream>(new MemoryStream());

        public Task DeleteAsync(string key, CancellationToken ct)
        {
            DeleteKeys.Add(key);
            return Task.CompletedTask;
        }
    }

    private static FileObject NewFile(Guid userId, string storageKey, string fileName) =>
        new()
        {
            UserId = userId,
            StorageKey = storageKey,
            FileName = fileName,
            ContentType = "text/plain",
            Size = 10
        };

    private sealed class TestTenantContext(Guid userId) : ITenantContext
    {
        public Guid? UserId { get; } = userId;
        public Guid? TenantId { get; } = Guid.CreateVersion7();
        public bool IsSystem => false;
        public string? IpAddress => "127.0.0.1";
    }

    private sealed class FailingOnceFileStorage(string failOnKey) : IFileStorage
    {
        private bool _failed;
        public List<string> DeleteKeys { get; } = [];

        public Task PutAsync(string key, Stream content, string contentType, CancellationToken ct) =>
            throw new NotSupportedException();

        public Task<Stream> GetAsync(string key, CancellationToken ct) =>
            throw new NotSupportedException();

        public Task DeleteAsync(string key, CancellationToken ct)
        {
            DeleteKeys.Add(key);
            if (!_failed && key == failOnKey)
            {
                _failed = true;
                throw new InvalidOperationException("storage delete failed once");
            }

            return Task.CompletedTask;
        }
    }
}
