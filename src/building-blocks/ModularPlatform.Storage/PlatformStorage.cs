using Amazon.S3;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using ModularPlatform.Abstractions;

namespace ModularPlatform.Storage;

/// <summary>
/// Registers the blob-storage building block. <see cref="IFileStorage"/> provider is selected by
/// <c>Storage:Provider</c> = <c>local</c> | <c>s3</c> (default <c>local</c>). Call once in the API host (and any
/// host that needs to read/write file bytes).
/// </summary>
public static class PlatformStorage
{
    public static IServiceCollection AddPlatformStorage(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<StorageOptions>()
            .Bind(configuration.GetSection(StorageOptions.SectionName))
            // Fail FAST at startup if S3 is selected without a bucket — otherwise the host boots healthy, passes health
            // checks, and only fails on the FIRST user upload with a cryptic AWS 403/malformed-request error.
            .Validate(o => !string.Equals(o.Provider, "s3", StringComparison.OrdinalIgnoreCase)
                || !string.IsNullOrWhiteSpace(o.S3.Bucket),
                "Storage:S3:Bucket is required when Storage:Provider=s3.")
            .ValidateOnStart();

        var provider = configuration.GetValue<string>($"{StorageOptions.SectionName}:Provider") ?? "local";

        if (string.Equals(provider, "s3", StringComparison.OrdinalIgnoreCase))
        {
            var s3Options = configuration.GetSection($"{StorageOptions.SectionName}:S3").Get<S3StorageOptions>()
                ?? new S3StorageOptions();
            services.TryAddSingleton<IAmazonS3>(_ => S3ClientFactory.Create(s3Options));
            services.AddSingleton<IFileStorage, S3FileStorage>();
        }
        else
        {
            services.AddSingleton<IFileStorage, LocalFileStorage>();
        }

        return services;
    }
}
