using Amazon.S3;
using Amazon.S3.Model;
using Beeps.Infrastructure.Storage;
using Beeps.Tests.Support;
using Microsoft.Extensions.Options;

namespace Beeps.Tests.Infrastructure.Storage;

public sealed class S3MediaStorageTests
{
    private readonly IAmazonS3 s3 = Substitute.For<IAmazonS3>();
    private readonly StorageOptions options = new() { ServiceUrl = "https://account.r2.cloudflarestorage.com", Bucket = "media", AccessKeyId = "id", SecretAccessKey = "secret" };

    [Fact]
    public async Task ObjectsArePublicImmutableAndUnsignedOverHttps()
    {
        PutObjectRequest? sent = null;
        s3.Config.Returns(new AmazonS3Config { ServiceURL = options.ServiceUrl });
        await s3.PutObjectAsync(Arg.Do<PutObjectRequest>(r => sent = r), Arg.Any<CancellationToken>());

        await Create().PutAsync("k/full.webp", new byte[] { 1, 2, 3 }, "image/webp", TestSupport.Ct);

        Assert.NotNull(sent);
        Assert.Equal("media", sent.BucketName);
        Assert.Equal("k/full.webp", sent.Key);
        Assert.Equal("image/webp", sent.ContentType);
        Assert.Equal("public, max-age=31536000, immutable", sent.Headers.CacheControl);
        Assert.True(sent.DisablePayloadSigning);
    }

    [Fact]
    public async Task PlainHttpSignsThePayload()
    {
        PutObjectRequest? sent = null;
        s3.Config.Returns(new AmazonS3Config { ServiceURL = "http://localhost:8333" });
        await s3.PutObjectAsync(Arg.Do<PutObjectRequest>(r => sent = r), Arg.Any<CancellationToken>());

        await Create().PutAsync("k/full.webp", new byte[] { 1 }, "image/webp", TestSupport.Ct);

        Assert.False(sent!.DisablePayloadSigning);
    }

    [Fact]
    public async Task DeletesGoInBatchesOfAThousand()
    {
        s3.DeleteObjectsAsync(default!, TestSupport.Ct).ReturnsForAnyArgs(new DeleteObjectsResponse());

        await Create().DeleteAsync([.. Enumerable.Range(0, 1500).Select(i => $"o{i}")], TestSupport.Ct);

        await s3.Received(1).DeleteObjectsAsync(Arg.Is<DeleteObjectsRequest>(r => r.Objects.Count == 1000 && r.BucketName == "media"), Arg.Any<CancellationToken>());
        await s3.Received(1).DeleteObjectsAsync(Arg.Is<DeleteObjectsRequest>(r => r.Objects.Count == 500), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AFailedDeleteThrowsSoTheOutboxRetries()
    {
        s3.DeleteObjectsAsync(default!, TestSupport.Ct).ReturnsForAnyArgs(new DeleteObjectsResponse { DeleteErrors = [new DeleteError { Key = "o1", Code = "InternalError" }] });

        await Assert.ThrowsAsync<InvalidOperationException>(() => Create().DeleteAsync(["o1"], TestSupport.Ct));
    }

    private S3MediaStorage Create() => new(s3, Options.Create(options));
}
