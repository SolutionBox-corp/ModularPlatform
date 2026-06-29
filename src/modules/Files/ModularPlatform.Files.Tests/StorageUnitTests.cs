using Microsoft.Extensions.Options;
using ModularPlatform.Cqrs;
using ModularPlatform.Storage;
using Shouldly;

namespace ModularPlatform.Files.Tests;

/// <summary>
/// Provider-level unit tests that need no live storage: the path-traversal guard on storage keys, and the S3 client
/// config wiring that makes the SAME provider target AWS S3 / MinIO / Cloudflare R2 by configuration alone.
/// A live S3 round-trip needs a MinIO Testcontainer or a real bucket and is intentionally NOT covered here.
/// </summary>
public sealed class StorageUnitTests
{
    [Theory]
    [InlineData("../etc/passwd")]
    [InlineData("/abs/path")]
    [InlineData("a\\b")]
    [InlineData("..")]
    [InlineData("")]
    [InlineData("a/../../b")]
    public void Invalid_storage_keys_are_rejected(string key)
    {
        Should.Throw<ArgumentException>(() => StorageKey.Validate(key));
    }

    [Theory]
    [InlineData("11111111111111111111111111111111/22222222222222222222222222222222")]
    [InlineData("user/file.bin")]
    public void Opaque_keys_are_accepted(string key)
    {
        Should.NotThrow(() => StorageKey.Validate(key));
    }

    [Fact]
    public async Task Local_missing_key_throws_file_not_found_error_code()
    {
        var storage = new LocalFileStorage(Options.Create(new StorageOptions
        {
            Local = new LocalStorageOptions
            {
                RootPath = Path.Combine(Path.GetTempPath(), $"modularplatform-storage-test-{Guid.CreateVersion7():N}"),
            },
        }));

        var exception = await Should.ThrowAsync<NotFoundException>(
            () => storage.GetAsync("11111111111111111111111111111111/missing.bin", CancellationToken.None));

        exception.ErrorCode.ShouldBe("file.not_found");
    }

    [Fact]
    public void S3_config_for_minio_uses_service_url_and_path_style()
    {
        var config = S3ClientFactory.BuildConfig(new S3StorageOptions
        {
            Bucket = "uploads",
            ServiceUrl = "http://localhost:9000",
            ForcePathStyle = true,
        });

        // The SDK normalizes the endpoint with a trailing slash.
        config.ServiceURL.TrimEnd('/').ShouldBe("http://localhost:9000");
        config.ForcePathStyle.ShouldBeTrue();
    }

    [Fact]
    public void S3_config_for_real_s3_uses_region_not_service_url()
    {
        var config = S3ClientFactory.BuildConfig(new S3StorageOptions
        {
            Bucket = "uploads",
            Region = "eu-central-1",
        });

        config.ServiceURL.ShouldBeNullOrEmpty();
        config.RegionEndpoint!.SystemName.ShouldBe("eu-central-1");
        config.ForcePathStyle.ShouldBeFalse();
    }
}
