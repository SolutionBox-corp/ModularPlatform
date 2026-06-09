using Amazon;
using Amazon.Runtime;
using Amazon.S3;

namespace ModularPlatform.Storage;

/// <summary>
/// Builds the <see cref="IAmazonS3"/> client + config from <see cref="S3StorageOptions"/>. Factored out (and the
/// config builder made internal-testable) so the endpoint/path-style wiring that makes the SAME provider work
/// against AWS S3, MinIO and Cloudflare R2 can be unit-tested without a live bucket.
/// </summary>
internal static class S3ClientFactory
{
    public static AmazonS3Config BuildConfig(S3StorageOptions options)
    {
        var config = new AmazonS3Config
        {
            // MinIO/R2 require path-style addressing (bucket in the path, not the host).
            ForcePathStyle = options.ForcePathStyle,
        };

        if (!string.IsNullOrWhiteSpace(options.ServiceUrl))
        {
            // Custom endpoint (MinIO / Cloudflare R2 / localstack). Takes precedence over region.
            config.ServiceURL = options.ServiceUrl;
        }
        else if (!string.IsNullOrWhiteSpace(options.Region))
        {
            config.RegionEndpoint = RegionEndpoint.GetBySystemName(options.Region);
        }

        return config;
    }

    public static IAmazonS3 Create(S3StorageOptions options)
    {
        var config = BuildConfig(options);

        if (!string.IsNullOrWhiteSpace(options.AccessKey) && !string.IsNullOrWhiteSpace(options.SecretKey))
        {
            return new AmazonS3Client(new BasicAWSCredentials(options.AccessKey, options.SecretKey), config);
        }

        // Fall back to the AWS default credential chain (env vars, profile, IAM role).
        return new AmazonS3Client(config);
    }
}
