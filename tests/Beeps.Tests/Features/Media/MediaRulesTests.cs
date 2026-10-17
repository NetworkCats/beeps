using Beeps.Features.Media;
using Beeps.Tests.Support;

namespace Beeps.Tests.Features.Media;

public sealed class MediaRulesTests
{
    [Fact]
    public void TheTypeComesFromTheMagicNumber()
    {
        Assert.Equal(ImageFormat.Png, MediaRules.Sniff(TestImages.Png()));
        Assert.Equal(ImageFormat.Jpeg, MediaRules.Sniff(TestImages.JpegWithExif(20, 20, 1)));
        Assert.Equal(ImageFormat.Gif, MediaRules.Sniff(TestImages.AnimatedGif()));
        Assert.Equal(ImageFormat.WebP, MediaRules.Sniff(TestImages.Quadrants(20, 20, SkiaSharp.SKEncodedImageFormat.Webp)));
    }

    [Theory]
    [InlineData("")]
    [InlineData("<svg xmlns=\"http://www.w3.org/2000/svg\"/>")]
    [InlineData("<html><script>alert(1)</script>")]
    [InlineData("RIFF\0\0\0\0WAVE")]
    [InlineData("GIF90a")]
    [InlineData("%PDF-1.7")]
    public void AnythingElseIsNoImage(string content) =>
        Assert.Null(MediaRules.Sniff(System.Text.Encoding.ASCII.GetBytes(content)));

    [Fact]
    public void ATruncatedWebPHeaderIsNoImage() => Assert.Null(MediaRules.Sniff("RIFF\0\0\0\0WEB"u8));

    [Theory]
    [InlineData(16, 16, MediaSizeProblem.None)]
    [InlineData(15, 400, MediaSizeProblem.TooSmall)]
    [InlineData(400, 15, MediaSizeProblem.TooSmall)]
    [InlineData(4032, 3024, MediaSizeProblem.None)]
    [InlineData(5120, 2880, MediaSizeProblem.None)]
    [InlineData(4096, 4096, MediaSizeProblem.None)]
    [InlineData(4097, 4096, MediaSizeProblem.TooLarge)]
    [InlineData(8193, 100, MediaSizeProblem.TooLarge)]
    [InlineData(100, 8193, MediaSizeProblem.TooLarge)]
    public void SizesAreCheckedPerSideAndInTotal(int width, int height, MediaSizeProblem expected) =>
        Assert.Equal(expected, MediaRules.CheckSize(width, height));

    [Theory]
    [InlineData(3000, 1000, 2048, 2048, 683)]
    [InlineData(1000, 3000, 2048, 683, 2048)]
    [InlineData(800, 600, 2048, 800, 600)]
    [InlineData(10000, 16, 800, 800, 1)]
    public void FittingKeepsTheShapeAndNeverEnlarges(int width, int height, int max, int expectedWidth, int expectedHeight) =>
        Assert.Equal((expectedWidth, expectedHeight), MediaRules.FitWithin(width, height, max));

    [Theory]
    [InlineData(0, 0, 100, 0, 0, 100)]
    [InlineData(100, 0, 100, 100, 0, 100)]
    // Rounding in the browser: an edge one or two pixels outside is pulled back in.
    [InlineData(101, 0, 100, 101, 0, 99)]
    [InlineData(-1, -1, 50, 0, 0, 49)]
    [InlineData(-2, 0, 100, 0, 0, 98)]
    public void ACropIsPulledInsideTheImage(int x, int y, int size, int expectedX, int expectedY, int expectedSize) =>
        Assert.Equal(new AvatarCrop(expectedX, expectedY, expectedSize), MediaRules.FitCrop(new AvatarCrop(x, y, size), 200, 100));

    [Theory]
    [InlineData(-3, 0, 50)]
    [InlineData(0, -3, 50)]
    [InlineData(150, 0, 60)]
    [InlineData(0, 0, 103)]
    [InlineData(0, 0, 15)]
    [InlineData(0, 0, 0)]
    [InlineData(0, 0, -20)]
    [InlineData(int.MaxValue, 0, int.MaxValue)]
    [InlineData(199, 99, 3)]
    public void ACropOutsideTheImageOrTooSmallIsRejected(int x, int y, int size) =>
        Assert.Null(MediaRules.FitCrop(new AvatarCrop(x, y, size), 200, 100));
}
