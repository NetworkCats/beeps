using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using Beeps.Features.Media;
using Microsoft.Extensions.Options;

namespace Beeps.Infrastructure.Storage;

/// <summary>
/// Media objects in an S3-compatible bucket. Objects are public and never change (a new upload gets a new
/// random name), so they are served with a year-long, immutable cache lifetime.
/// </summary>
public sealed class S3MediaStorage(IAmazonS3 s3, IOptions<StorageOptions> options) : IMediaStorage
{
    private const string CacheControl = "public, max-age=31536000, immutable";

    /// <summary>The S3 limit for one DeleteObjects call.</summary>
    private const int MaxDeleteBatch = 1000;

    public static IAmazonS3 CreateClient(StorageOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        return new AmazonS3Client(
            new BasicAWSCredentials(options.AccessKeyId, options.SecretAccessKey),
            new AmazonS3Config
            {
                ServiceURL = options.ServiceUrl,
                AuthenticationRegion = options.Region,
                ForcePathStyle = true,
                // R2 and SeaweedFS do not implement every checksum newer SDKs send by default.
                RequestChecksumCalculation = RequestChecksumCalculation.WHEN_REQUIRED,
                ResponseChecksumValidation = ResponseChecksumValidation.WHEN_REQUIRED,
                Timeout = TimeSpan.FromSeconds(30),
            });
    }

    public async Task PutAsync(string objectName, ReadOnlyMemory<byte> content, string contentType, CancellationToken cancellationToken)
    {
        using var stream = new MemoryStream(content.ToArray(), writable: false);
        await s3.PutObjectAsync(
            new PutObjectRequest
            {
                BucketName = options.Value.Bucket,
                Key = objectName,
                InputStream = stream,
                ContentType = contentType,
                Headers = { CacheControl = CacheControl },
                // R2 does not support the streaming SigV4 signature the SDK uses otherwise; the SDK only
                // allows an unsigned payload over HTTPS, and plain HTTP is only ever local SeaweedFS.
                DisablePayloadSigning = s3.Config.ServiceURL?.StartsWith("https://", StringComparison.OrdinalIgnoreCase) == true,
                DisableDefaultChecksumValidation = true,
            },
            cancellationToken);
    }

    public async Task DeleteAsync(IReadOnlyCollection<string> objectNames, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(objectNames);
        foreach (var batch in objectNames.Chunk(MaxDeleteBatch))
        {
            var response = await s3.DeleteObjectsAsync(
                new DeleteObjectsRequest
                {
                    BucketName = options.Value.Bucket,
                    Objects = [.. batch.Select(name => new KeyVersion { Key = name })],
                    Quiet = true,
                },
                cancellationToken);
            if (response.DeleteErrors is { Count: > 0 } errors)
            {
                throw new InvalidOperationException($"Deleting {errors.Count} media objects failed, e.g. {errors[0].Key}: {errors[0].Code}");
            }
        }
    }
}
