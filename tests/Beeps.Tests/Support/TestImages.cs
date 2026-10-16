using Beeps.Features.Media;
using Beeps.Features.RateLimiting;
using Beeps.Infrastructure.Media;
using Microsoft.Extensions.Logging.Abstractions;
using SkiaSharp;

namespace Beeps.Tests.Support;

/// <summary>Real images, made in memory, for the image pipeline and the upload endpoints.</summary>
internal static class TestImages
{
    public static readonly SKColor TopLeft = SKColors.Red;
    public static readonly SKColor TopRight = SKColors.Lime;
    public static readonly SKColor BottomLeft = SKColors.Blue;
    public static readonly SKColor BottomRight = SKColors.Yellow;

    /// <summary>Text hidden in an EXIF block, to prove that metadata does not survive.</summary>
    public const string SecretMarker = "GPS-SECRET-MARKER 37.7749N 122.4194W";

    /// <summary>Four colored quadrants, so orientation and cropping can be checked by color.</summary>
    public static byte[] Quadrants(int width, int height, SKEncodedImageFormat format = SKEncodedImageFormat.Png)
    {
        using var bitmap = new SKBitmap(width, height);
        using (var canvas = new SKCanvas(bitmap))
        {
            using var paint = new SKPaint();
            paint.Color = TopLeft;
            canvas.DrawRect(0, 0, width / 2f, height / 2f, paint);
            paint.Color = TopRight;
            canvas.DrawRect(width / 2f, 0, width / 2f, height / 2f, paint);
            paint.Color = BottomLeft;
            canvas.DrawRect(0, height / 2f, width / 2f, height / 2f, paint);
            paint.Color = BottomRight;
            canvas.DrawRect(width / 2f, height / 2f, width / 2f, height / 2f, paint);
        }

        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(format, 95);
        return data.ToArray();
    }

    public static byte[] Png(int width = 64, int height = 48) => Quadrants(width, height);

    /// <summary>A JPEG whose EXIF says how to turn it (6: 90° clockwise) and that carries <see cref="SecretMarker"/>.</summary>
    public static byte[] JpegWithExif(int width, int height, ushort orientation)
    {
        var jpeg = Quadrants(width, height, SKEncodedImageFormat.Jpeg);
        byte[] tiff = [0x4D, 0x4D, 0x00, 0x2A, 0, 0, 0, 8, 0, 1, 0x01, 0x12, 0, 3, 0, 0, 0, 1, (byte)(orientation >> 8), (byte)orientation, 0, 0, 0, 0, 0, 0];
        byte[] exif = [.. "Exif\0\0"u8, .. tiff, .. System.Text.Encoding.ASCII.GetBytes(SecretMarker)];
        var length = exif.Length + 2;
        byte[] app1 = [0xFF, 0xE1, (byte)(length >> 8), (byte)length, .. exif];
        return [.. jpeg.AsSpan(0, 2), .. app1, .. jpeg.AsSpan(2)];
    }

    /// <summary>A two-frame GIF: the first frame is all red, the second all blue.</summary>
    public static byte[] AnimatedGif()
    {
        static byte[] Frame(byte color)
        {
            List<int> codes = [];
            for (var i = 0; i < 16 * 16; i++)
            {
                if (i % 2 == 0)
                {
                    codes.Add(4);
                }

                codes.Add(color);
            }

            codes.Add(5);
            List<byte> packed = [];
            int bits = 0, count = 0;
            foreach (var code in codes)
            {
                bits |= code << count;
                count += 3;
                while (count >= 8)
                {
                    packed.Add((byte)(bits & 0xFF));
                    bits >>= 8;
                    count -= 8;
                }
            }

            if (count > 0)
            {
                packed.Add((byte)bits);
            }

            List<byte> frame = [0x21, 0xF9, 0x04, 0x00, 0x0A, 0x00, 0x00, 0x00, 0x2C, 0, 0, 0, 0, 16, 0, 16, 0, 0x00, 0x02];
            foreach (var chunk in packed.Chunk(255))
            {
                frame.Add((byte)chunk.Length);
                frame.AddRange(chunk);
            }

            frame.Add(0);
            return [.. frame];
        }

        byte[] header = [.. "GIF89a"u8, 16, 0, 16, 0, 0x91, 0, 0, 0xFF, 0, 0, 0, 0, 0xFF, 0, 0, 0, 0, 0, 0];
        byte[] loop = [0x21, 0xFF, 0x0B, .. "NETSCAPE2.0"u8, 0x03, 0x01, 0x00, 0x00, 0x00];
        return [.. header, .. loop, .. Frame(0), .. Frame(1), 0x3B];
    }

    public static SKColor ColorAt(byte[] encoded, float x, float y)
    {
        using var bitmap = SKBitmap.Decode(encoded);
        return bitmap.GetPixel((int)(x * (bitmap.Width - 1)), (int)(y * (bitmap.Height - 1)));
    }

    public static (int Width, int Height) SizeOf(byte[] encoded)
    {
        using var codec = SKCodec.Create(SKData.CreateCopy(encoded));
        return (codec.Info.Width, codec.Info.Height);
    }

    /// <summary>Close enough after lossy WebP: each channel within a margin.</summary>
    public static bool Near(SKColor actual, SKColor expected) =>
        Math.Abs(actual.Red - expected.Red) < 40 && Math.Abs(actual.Green - expected.Green) < 40 && Math.Abs(actual.Blue - expected.Blue) < 40;

    public static MediaContent Content(byte[] data) => new(new MemoryStream(data), data.Length);
}

/// <summary>A real <see cref="MediaService"/> over mocks, as other Services' tests need one.</summary>
internal sealed class MediaServiceParts
{
    public IImageProcessor Images { get; init; } = Substitute.For<IImageProcessor>();

    public IMediaStorage Storage { get; } = Substitute.For<IMediaStorage>();

    public IMediaStore Store { get; } = Substitute.For<IMediaStore>();

    public IRateLimitStore RateLimits { get; } = Substitute.For<IRateLimitStore>();

    public ImmediateUnitOfWork UnitOfWork { get; init; } = new();

    public MediaServiceParts() =>
        RateLimits.TryAcquireAsync(default!, CancellationToken.None).ReturnsForAnyArgs(RateLimitDecision.Allowed);

    /// <summary>Stores exactly the ids it is asked to lock: every image belongs to the caller.</summary>
    public void OwnEverything() =>
        Store.LockOwnedAsync(default, default!, CancellationToken.None)
            .ReturnsForAnyArgs(ci => ci.Arg<IReadOnlyCollection<Guid>>() is { } ids ? (IReadOnlyList<Guid>)[.. ids] : []);

    public MediaService Create() => new(
        Images,
        Storage,
        Store,
        UnitOfWork,
        TestData.Media,
        TestData.RateLimits(RateLimits),
        TestData.Time(),
        new TestLocalizer(),
        NullLogger<MediaService>.Instance);

    /// <summary>The real Skia processor: uploads in these tests go through actual decoding.</summary>
    public static MediaServiceParts WithRealImages(ImmediateUnitOfWork? unitOfWork = null) =>
        new() { Images = new SkiaImageProcessor(), UnitOfWork = unitOfWork ?? new ImmediateUnitOfWork() };
}
