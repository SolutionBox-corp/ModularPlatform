using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.Extensions.Options;
using ModularPlatform.Abstractions;

namespace ModularPlatform.Storage;

/// <summary>
/// S3-compatible <see cref="IFileStorage"/> over the AWS SDK. Works against AWS S3, MinIO and Cloudflare R2 — the
/// difference is purely config (<see cref="S3StorageOptions.ServiceUrl"/> + <see cref="S3StorageOptions.ForcePathStyle"/>).
/// The bucket and endpoint come ONLY from config; the key is the server-generated opaque id.
/// </summary>
internal sealed class S3FileStorage : IFileStorage
{
    private readonly IAmazonS3 _client;
    private readonly string _bucket;

    public S3FileStorage(IAmazonS3 client, IOptions<StorageOptions> options)
    {
        _client = client;
        _bucket = options.Value.S3.Bucket;
    }

    public async Task PutAsync(string key, Stream content, string contentType, CancellationToken ct)
    {
        StorageKey.Validate(key);
        var request = new PutObjectRequest
        {
            BucketName = _bucket,
            Key = key,
            InputStream = content,
            ContentType = contentType,
            AutoCloseStream = false,
        };
        await _client.PutObjectAsync(request, ct);
    }

    public async Task<Stream> GetAsync(string key, CancellationToken ct)
    {
        StorageKey.Validate(key);
        var response = await _client.GetObjectAsync(new GetObjectRequest { BucketName = _bucket, Key = key }, ct);
        return response.ResponseStream;
    }

    public async Task DeleteAsync(string key, CancellationToken ct)
    {
        StorageKey.Validate(key);
        await _client.DeleteObjectAsync(new DeleteObjectRequest { BucketName = _bucket, Key = key }, ct);
    }
}
