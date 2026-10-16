using System.Text;
using Beeps.Features.Media;
using Beeps.Infrastructure.Media;
using Beeps.Tests.Support;
using SkiaSharp;

namespace Beeps.Tests.Infrastructure.Media;

/// <summary>The real image pipeline over real (in-memory) images: PLAN 6.11 is about what comes out.</summary>
public sealed class SkiaImageProcessorTests
{
    private readonly SkiaImageProcessor processor = new();

    [Fact]
    public void TheHeaderGivesFormatAndSizeWithoutDecoding()
    {
        Assert.Equal(new ImageHeader(ImageFormat.Png, 64, 48), processor.ReadHeader(TestImages.Png(64, 48)));
        Assert.Equal(new ImageHeader(ImageFormat.Gif, 16, 16), processor.ReadHeader(TestImages.AnimatedGif()));
        Assert.Null(processor.ReadHeader("not an image at all"u8.ToArray()));
    }

    [Fact]
    public void EverySizeIsWebPWithoutAnyMetadata()
    {
        var variants = processor.Encode(TestImages.JpegWithExif(400, 200, orientation: 1))!;

        foreach (var output in new[] { variants.Full, variants.Thumbnail, variants.Avatar })
        {
            Assert.Equal(ImageFormat.WebP, MediaRules.Sniff(output));
            Assert.DoesNotContain(TestImages.SecretMarker, Encoding.ASCII.GetString(output), StringComparison.Ordinal);
            Assert.DoesNotContain("Exif", Encoding.ASCII.GetString(output), StringComparison.Ordinal);
        }
    }

    [Fact]
    public void ThePhotoIsTurnedUprightByItsExifOrientation()
    {
        // Stored 400 × 200 on its side; orientation 6 means "turn 90° clockwise to view".
        var variants = processor.Encode(TestImages.JpegWithExif(400, 200, orientation: 6))!;

        Assert.Equal((200, 400), (variants.Width, variants.Height));
        Assert.Equal((200, 400), TestImages.SizeOf(variants.Full));
        // Turned clockwise, the stored bottom-left corner is now at the top left.
        Assert.True(TestImages.Near(TestImages.ColorAt(variants.Full, 0.1f, 0.1f), TestImages.BottomLeft));
        Assert.True(TestImages.Near(TestImages.ColorAt(variants.Full, 0.9f, 0.1f), TestImages.TopLeft));
        Assert.True(TestImages.Near(TestImages.ColorAt(variants.Full, 0.1f, 0.9f), TestImages.BottomRight));
        Assert.True(TestImages.Near(TestImages.ColorAt(variants.Full, 0.9f, 0.9f), TestImages.TopRight));
    }

    [Theory]
    [InlineData(SKEncodedOrigin.TopRight, 400, 200)]
    [InlineData(SKEncodedOrigin.BottomRight, 400, 200)]
    [InlineData(SKEncodedOrigin.BottomLeft, 400, 200)]
    [InlineData(SKEncodedOrigin.LeftTop, 200, 400)]
    [InlineData(SKEncodedOrigin.RightTop, 200, 400)]
    [InlineData(SKEncodedOrigin.RightBottom, 200, 400)]
    [InlineData(SKEncodedOrigin.LeftBottom, 200, 400)]
    public void EveryOrientationMapsTheImageOntoItsUprightSize(SKEncodedOrigin origin, int width, int height)
    {
        var matrix = SkiaImageProcessor.OrientationMatrix(origin, 400, 200);
        var corners = new[] { new SKPoint(0, 0), new SKPoint(400, 0), new SKPoint(0, 200), new SKPoint(400, 200) }.Select(matrix.MapPoint).ToList();

        Assert.Equal(0, corners.Min(p => p.X), 3);
        Assert.Equal(0, corners.Min(p => p.Y), 3);
        Assert.Equal(width, corners.Max(p => p.X), 3);
        Assert.Equal(height, corners.Max(p => p.Y), 3);
    }

    [Fact]
    public void LargeImagesAreScaledDownAndTheAvatarIsACenteredSquare()
    {
        var variants = processor.Encode(TestImages.Quadrants(3000, 1000))!;

        Assert.Equal((2048, 683), (variants.Width, variants.Height));
        Assert.Equal((2048, 683), TestImages.SizeOf(variants.Full));
        Assert.Equal((800, 267), TestImages.SizeOf(variants.Thumbnail));
        Assert.Equal((MediaRules.AvatarSide, MediaRules.AvatarSide), TestImages.SizeOf(variants.Avatar));
        // The middle third of a 3:1 image: the left half of the square comes from the top-left quadrant.
        Assert.True(TestImages.Near(TestImages.ColorAt(variants.Avatar, 0.25f, 0.25f), TestImages.TopLeft));
        Assert.True(TestImages.Near(TestImages.ColorAt(variants.Avatar, 0.75f, 0.75f), TestImages.BottomRight));
    }

    [Fact]
    public void TheHeaderSizeIsUprightLikeTheBrowserShowsIt() =>
        Assert.Equal(new ImageHeader(ImageFormat.Jpeg, 200, 400), processor.ReadHeader(TestImages.JpegWithExif(400, 200, orientation: 6)));

    [Fact]
    public void AnAvatarCropIsTakenFromTheUprightImage()
    {
        // Upright, the photo is 200 × 400 and its top-left quadrant is the stored bottom-left one.
        var variants = processor.Encode(TestImages.JpegWithExif(400, 200, orientation: 6), new AvatarCrop(0, 0, 100))!;

        Assert.Equal((100, 100), TestImages.SizeOf(variants.Avatar));
        Assert.True(TestImages.Near(TestImages.ColorAt(variants.Avatar, 0.5f, 0.5f), TestImages.BottomLeft));
        // The other sizes are not cropped.
        Assert.Equal((200, 400), TestImages.SizeOf(variants.Full));
    }

    [Fact]
    public void ALargeCropIsScaledDownToTheAvatarSize()
    {
        var variants = processor.Encode(TestImages.Quadrants(1200, 1000), new AvatarCrop(600, 500, 500))!;

        Assert.Equal((MediaRules.AvatarSide, MediaRules.AvatarSide), TestImages.SizeOf(variants.Avatar));
        Assert.True(TestImages.Near(TestImages.ColorAt(variants.Avatar, 0.5f, 0.5f), TestImages.BottomRight));
    }

    [Fact]
    public void SmallImagesAreNeverEnlarged()
    {
        var variants = processor.Encode(TestImages.Png(64, 48))!;

        Assert.Equal((64, 48), TestImages.SizeOf(variants.Full));
        Assert.Equal((48, 48), TestImages.SizeOf(variants.Avatar));
    }

    [Fact]
    public void AnAnimationKeepsOnlyItsFirstFrame()
    {
        var variants = processor.Encode(TestImages.AnimatedGif())!;

        Assert.True(TestImages.Near(TestImages.ColorAt(variants.Full, 0.5f, 0.5f), SKColors.Red));
        Assert.DoesNotContain("ANIM", Encoding.ASCII.GetString(variants.Full), StringComparison.Ordinal);
    }

    [Fact]
    public void ATruncatedFileIsRejectedNotHalfDecoded()
    {
        var jpeg = TestImages.Quadrants(400, 200, SKEncodedImageFormat.Jpeg);

        Assert.NotNull(processor.ReadHeader(jpeg.AsMemory(0, jpeg.Length / 2)));
        Assert.Null(processor.Encode(jpeg.AsMemory(0, jpeg.Length / 2)));
    }

    [Fact]
    public void GarbageIsRejected() => Assert.Null(processor.Encode("GIF89a but not really"u8.ToArray()));
}
