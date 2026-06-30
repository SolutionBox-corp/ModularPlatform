using Amazon.S3;
using Amazon.S3.Model;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Microsoft.Extensions.Options;
using ModularPlatform.Cqrs;
using ModularPlatform.Storage;
using Shouldly;

namespace ModularPlatform.BuildingBlocks.Tests;

public sealed class S3FileStorageMinioTests : IAsyncLifetime
{
    private const string AccessKey = "minioadmin";
    private const string SecretKey = "minioadmin";
    private const string BucketName = "platform-files";

    private readonly IContainer _minio = new ContainerBuilder("minio/minio:RELEASE.2025-04-22T22-12-26Z")
        .WithPortBinding(9000, assignRandomHostPort: true)
        .WithEnvironment("MINIO_ROOT_USER", AccessKey)
        .WithEnvironment("MINIO_ROOT_PASSWORD", SecretKey)
        .WithCommand("server", "/data")
        .WithWaitStrategy(Wait.ForUnixContainer().UntilInternalTcpPortIsAvailable(9000))
        .Build();

    private IAmazonS3? _client;

    public async Task InitializeAsync()
    {
        await _minio.StartAsync();

        var s3Options = CreateS3Options();
        _client = S3ClientFactory.Create(s3Options);
        await _client.PutBucketAsync(new PutBucketRequest { BucketName = BucketName });
    }

    public async Task DisposeAsync()
    {
        _client?.Dispose();
        await _minio.DisposeAsync();
    }

    [Fact]
    public async Task S3_provider_round_trips_bytes_against_minio_and_maps_missing_objects_to_file_not_found()
    {
        var storage = CreateStorage();
        var key = $"{Guid.CreateVersion7():N}/{Guid.CreateVersion7():N}";
        var payload = "hello from minio"u8.ToArray();
        await using var upload = new MemoryStream(payload);

        await storage.PutAsync(key, upload, "text/plain", CancellationToken.None);

        upload.CanRead.ShouldBeTrue("S3FileStorage must not close the caller-owned upload stream.");

        await using (var downloaded = await storage.GetAsync(key, CancellationToken.None))
        await using (var copy = new MemoryStream())
        {
            await downloaded.CopyToAsync(copy);
            copy.ToArray().ShouldBe(payload);
        }

        await storage.DeleteAsync(key, CancellationToken.None);

        var missing = await Should.ThrowAsync<NotFoundException>(
            () => storage.GetAsync(key, CancellationToken.None));
        missing.ErrorCode.ShouldBe("file.not_found");

        await storage.DeleteAsync(key, CancellationToken.None);
    }

    private S3FileStorage CreateStorage()
    {
        var options = new StorageOptions
        {
            Provider = "s3",
            S3 = CreateS3Options(),
        };
        return new S3FileStorage(_client!, Options.Create(options));
    }

    private S3StorageOptions CreateS3Options() =>
        new()
        {
            Bucket = BucketName,
            ServiceUrl = $"http://{_minio.Hostname}:{_minio.GetMappedPublicPort(9000)}",
            ForcePathStyle = true,
            AccessKey = AccessKey,
            SecretKey = SecretKey,
        };
}
