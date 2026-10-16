using Beeps.Features.Beeps;
using Beeps.Features.Media;
using Beeps.Features.RateLimiting;
using Beeps.Features.Shared;
using Beeps.Tests.Support;

namespace Beeps.Tests.Features.Media;

/// <summary>Uploads through the real SkiaSharp processor; storage, store and rate limits are mocks.</summary>
public sealed class MediaServiceTests
{
    private static readonly Viewer Agent = Viewer.Agent(Guid.NewGuid(), Guid.NewGuid());

    private readonly MediaServiceParts parts = MediaServiceParts.WithRealImages();

    [Fact]
    public async Task AnUploadIsStoredInEverySizeUnderARandomName()
    {
        MediaItem? stored = null;
        parts.Store.When(s => s.AddMedia(Arg.Any<MediaItem>())).Do(ci => stored = ci.Arg<MediaItem>());

        var result = await parts.Create().UploadAsync(Agent, TestImages.Content(TestImages.Quadrants(3000, 1000)), TestSupport.Ct);

        Assert.True(result.IsSuccess);
        Assert.NotNull(stored);
        Assert.Equal(Agent.ActorId, stored.OwnerActorId);
        Assert.Matches("^[0-9a-f]{32}$", stored.StorageKey);
        Assert.Equal((2048, 683), (stored.Width, stored.Height));
        foreach (var name in MediaObjects.All(stored.StorageKey))
        {
            await parts.Storage.Received(1).PutAsync(name, Arg.Is<ReadOnlyMemory<byte>>(m => IsWebP(m)), "image/webp", Arg.Any<CancellationToken>());
        }

        Assert.Equal(1, parts.UnitOfWork.Saves);
        Assert.Equal(new MediaView(stored.Id, TestData.Media.Full(stored.StorageKey), TestData.Media.Thumbnail(stored.StorageKey), 2048, 683), result.Value);
    }

    [Fact]
    public async Task TwoUploadsOfTheSameFileGetDifferentNames()
    {
        List<string> keys = [];
        parts.Store.When(s => s.AddMedia(Arg.Any<MediaItem>())).Do(ci => keys.Add(ci.Arg<MediaItem>().StorageKey));
        var service = parts.Create();

        await service.UploadAsync(Agent, TestImages.Content(TestImages.Png()), TestSupport.Ct);
        await service.UploadAsync(Agent, TestImages.Content(TestImages.Png()), TestSupport.Ct);

        Assert.Equal(2, keys.Distinct().Count());
    }

    [Fact]
    public async Task UploadsCountAgainstTheUploadQuotaOfTheUploader()
    {
        parts.RateLimits.TryAcquireAsync(default!, TestSupport.Ct).ReturnsForAnyArgs(RateLimitDecision.Denied(TimeSpan.FromMinutes(5)));

        var result = await parts.Create().UploadAsync(Agent, TestImages.Content(TestImages.Png()), TestSupport.Ct);

        Assert.Equal(ErrorKind.RateLimited, result.Error?.Kind);
        await parts.RateLimits.Received(1).TryAcquireAsync(
            Arg.Is<IReadOnlyList<RateLimitCounter>>(c => TestData.Policies(c).Single() == RateLimitPolicies.MediaUpload), Arg.Any<CancellationToken>());
        await parts.Storage.DidNotReceiveWithAnyArgs().PutAsync(default!, default, default!, TestSupport.Ct);
    }

    [Fact]
    public async Task ADeclaredLengthOverTheLimitIsRejectedWithoutReading()
    {
        var stream = Substitute.For<Stream>();

        var result = await parts.Create().UploadAsync(Agent, new MediaContent(stream, MediaRules.MaxBytes + 1), TestSupport.Ct);

        Assert.Equal(MediaErrorCodes.TooLarge, result.Error?.Code);
        Assert.Equal(ErrorKind.TooLarge, result.Error?.Kind);
        Assert.DoesNotContain(stream.ReceivedCalls(), c => c.GetMethodInfo().Name.StartsWith("Read", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ABodyOverTheLimitIsRejectedEvenWithoutADeclaredLength()
    {
        var data = new byte[MediaRules.MaxBytes + 1];
        TestImages.Png().CopyTo(data, 0);

        var result = await parts.Create().UploadAsync(Agent, new MediaContent(new MemoryStream(data), null), TestSupport.Ct);

        Assert.Equal(MediaErrorCodes.TooLarge, result.Error?.Code);
    }

    [Fact]
    public async Task ExactlyTheLimitIsStillAccepted()
    {
        var png = TestImages.Png();
        var data = new byte[MediaRules.MaxBytes];
        png.CopyTo(data, 0);

        var result = await parts.Create().UploadAsync(Agent, new MediaContent(new MemoryStream(data), null), TestSupport.Ct);

        Assert.True(result.IsSuccess, result.Error?.Code);
    }

    [Theory]
    [InlineData("hello", MediaErrorCodes.TypeUnsupported)]
    [InlineData("<svg xmlns=\"http://www.w3.org/2000/svg\"/>", MediaErrorCodes.TypeUnsupported)]
    [InlineData("", MediaErrorCodes.TypeUnsupported)]
    public async Task OnlyRealImageTypesAreAccepted(string content, string code)
    {
        var result = await parts.Create().UploadAsync(Agent, TestImages.Content(System.Text.Encoding.UTF8.GetBytes(content)), TestSupport.Ct);

        Assert.Equal(code, result.Error?.Code);
        parts.Store.DidNotReceiveWithAnyArgs().AddMedia(default!);
    }

    [Fact]
    public async Task AMagicNumberWithNothingBehindItIsUnreadable()
    {
        byte[] fake = [0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A, .. "<script>alert(1)</script>"u8];

        var result = await parts.Create().UploadAsync(Agent, TestImages.Content(fake), TestSupport.Ct);

        Assert.Equal(MediaErrorCodes.Unreadable, result.Error?.Code);
    }

    [Fact]
    public async Task AFileWhoseHeaderDisagreesWithItsMagicNumberIsRejected()
    {
        var images = Substitute.For<IImageProcessor>();
        images.ReadHeader(default).ReturnsForAnyArgs(new ImageHeader(ImageFormat.Gif, 100, 100));
        var service = new MediaServiceParts { Images = images }.Create();

        var result = await service.UploadAsync(Agent, TestImages.Content(TestImages.Png()), TestSupport.Ct);

        Assert.Equal(MediaErrorCodes.Unreadable, result.Error?.Code);
        images.DidNotReceiveWithAnyArgs().Encode(default);
    }

    [Fact]
    public async Task SizesAreCheckedFromTheHeaderBeforeDecoding()
    {
        var images = Substitute.For<IImageProcessor>();
        images.ReadHeader(default).ReturnsForAnyArgs(new ImageHeader(ImageFormat.Png, 50_000, 50_000), new ImageHeader(ImageFormat.Png, 8, 8));
        var service = new MediaServiceParts { Images = images }.Create();

        var huge = await service.UploadAsync(Agent, TestImages.Content(TestImages.Png()), TestSupport.Ct);
        var tiny = await service.UploadAsync(Agent, TestImages.Content(TestImages.Png()), TestSupport.Ct);

        Assert.Equal(MediaErrorCodes.DimensionsTooLarge, huge.Error?.Code);
        Assert.Equal(MediaErrorCodes.DimensionsTooSmall, tiny.Error?.Code);
        images.DidNotReceiveWithAnyArgs().Encode(default);
    }

    [Fact]
    public async Task AnAvatarCropIsCheckedAgainstTheUprightSizeAndPassedOnFitted()
    {
        var images = Substitute.For<IImageProcessor>();
        images.ReadHeader(default).ReturnsForAnyArgs(new ImageHeader(ImageFormat.Png, 200, 100));
        images.Encode(default, default).ReturnsForAnyArgs(new ImageVariants([1], 200, 100, [2], [3]));
        var service = new MediaServiceParts { Images = images }.Create();

        var fitted = await service.UploadAsync(Agent, TestImages.Content(TestImages.Png()), new AvatarCrop(150, 0, 52), TestSupport.Ct);
        var outside = await service.UploadAsync(Agent, TestImages.Content(TestImages.Png()), new AvatarCrop(150, 0, 60), TestSupport.Ct);

        Assert.True(fitted.IsSuccess);
        images.Received(1).Encode(Arg.Any<ReadOnlyMemory<byte>>(), new AvatarCrop(150, 0, 50));
        Assert.Equal(MediaErrorCodes.AvatarCropInvalid, outside.Error?.Code);
        images.ReceivedWithAnyArgs(1).Encode(default, default);
    }

    [Fact]
    public async Task AnImageThatFailsToDecodeIsRejected()
    {
        var jpeg = TestImages.Quadrants(400, 200, SkiaSharp.SKEncodedImageFormat.Jpeg);

        var result = await parts.Create().UploadAsync(Agent, TestImages.Content(jpeg[..(jpeg.Length / 2)]), TestSupport.Ct);

        Assert.Equal(MediaErrorCodes.Unreadable, result.Error?.Code);
        await parts.Storage.DidNotReceiveWithAnyArgs().PutAsync(default!, default, default!, TestSupport.Ct);
    }

    [Fact]
    public async Task WhenTheRowCannotBeSavedTheObjectsAreRemovedAgain()
    {
        parts.UnitOfWork.FailNextSave = new InvalidOperationException("database down");

        await Assert.ThrowsAsync<InvalidOperationException>(() => parts.Create().UploadAsync(Agent, TestImages.Content(TestImages.Png()), TestSupport.Ct));

        await parts.Storage.Received(1).DeleteAsync(Arg.Is<IReadOnlyCollection<string>>(names => names.Count == 3), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task UploadingSeveralStopsAtTheFirstFailure()
    {
        List<MediaContent> files = [TestImages.Content(TestImages.Png()), TestImages.Content("nope"u8.ToArray()), TestImages.Content(TestImages.Png())];

        var result = await parts.Create().UploadAllAsync(Agent, files, TestSupport.Ct);

        Assert.Equal(MediaErrorCodes.TypeUnsupported, result.Error?.Code);
        parts.Store.Received(1).AddMedia(Arg.Any<MediaItem>());
    }

    [Fact]
    public async Task UploadingSeveralReturnsTheirIdsInOrder()
    {
        List<Guid> added = [];
        parts.Store.When(s => s.AddMedia(Arg.Any<MediaItem>())).Do(ci => added.Add(ci.Arg<MediaItem>().Id));

        var result = await parts.Create().UploadAllAsync(Agent, [TestImages.Content(TestImages.Png()), TestImages.Content(TestImages.AnimatedGif())], TestSupport.Ct);

        Assert.Equal(added, result.Value);
    }

    [Fact]
    public async Task MoreThanABeepCanHoldIsRejectedBeforeAnyUpload()
    {
        var files = Enumerable.Range(0, MediaRules.MaxImagesPerBeep + 1).Select(_ => TestImages.Content(TestImages.Png())).ToList();

        var result = await parts.Create().UploadAllAsync(Agent, files, TestSupport.Ct);

        Assert.Equal(MediaErrorCodes.TooMany, result.Error?.Code);
        await parts.RateLimits.DidNotReceiveWithAnyArgs().TryAcquireAsync(default!, TestSupport.Ct);
    }

    private static bool IsWebP(ReadOnlyMemory<byte> data) => MediaRules.Sniff(data.Span) == ImageFormat.WebP;

    [Fact]
    public void ErrorsForMediaIdsNameTheField()
    {
        var service = parts.Create();

        Assert.Equal([MediaFields.MediaIds], service.NotFound(MediaFields.MediaIds).Fields!.Keys);
        Assert.Equal([MediaFields.Images], service.TooMany(MediaFields.Images).Fields!.Keys);
        Assert.Equal(MediaErrorCodes.RawBodyRequired, service.RawBodyRequired().Code);
    }
}
