using Beeps.Features.Media;
using SkiaSharp;

namespace Beeps.Infrastructure.Media;

/// <summary>
/// SkiaSharp (MIT) implementation of <see cref="IImageProcessor"/>. Every size is drawn from the decoded
/// pixels onto a fresh sRGB surface and encoded from there, so nothing from the original file but its
/// pixels survives: no EXIF, GPS, comments or ICC profiles.
/// </summary>
public sealed class SkiaImageProcessor : IImageProcessor
{
    private const float FullQuality = 85;

    private const float ThumbnailQuality = 80;

    private const float AvatarQuality = 85;

    // Downscaling by large factors needs mipmaps, or fine detail turns into noise.
    private static readonly SKSamplingOptions Sampling = new(SKFilterMode.Linear, SKMipmapMode.Linear);

    public ImageHeader? ReadHeader(ReadOnlyMemory<byte> data)
    {
        using var skData = SKData.CreateCopy(data.Span);
        using var codec = SKCodec.Create(skData);
        if (codec is null || FormatOf(codec.EncodedFormat) is not { } format)
        {
            return null;
        }

        var (width, height) = Upright(codec.Info.Width, codec.Info.Height, codec.EncodedOrigin);
        return new ImageHeader(format, width, height);
    }

    public ImageVariants? Encode(ReadOnlyMemory<byte> data, AvatarCrop? avatarCrop = null)
    {
        using var skData = SKData.CreateCopy(data.Span);
        using var codec = SKCodec.Create(skData);
        if (codec is null)
        {
            return null;
        }

        using var decoded = Decode(codec);
        if (decoded is null)
        {
            return null;
        }

        using var image = SKImage.FromBitmap(decoded);
        var origin = codec.EncodedOrigin;
        var (width, height) = Upright(image.Width, image.Height, origin);
        var whole = new SKRect(0, 0, width, height);
        var (fullWidth, fullHeight) = MediaRules.FitWithin(width, height, MediaRules.FullMaxSide);
        var (thumbWidth, thumbHeight) = MediaRules.FitWithin(width, height, MediaRules.ThumbnailMaxSide);
        var side = avatarCrop?.Size ?? Math.Min(width, height);
        var square = avatarCrop is { } crop
            ? SKRect.Create(crop.X, crop.Y, crop.Size, crop.Size)
            : SKRect.Create((width - side) / 2f, (height - side) / 2f, side, side);
        var avatarSide = Math.Min(side, MediaRules.AvatarSide);

        var full = Render(image, origin, whole, fullWidth, fullHeight, FullQuality);
        var thumbnail = Render(image, origin, whole, thumbWidth, thumbHeight, ThumbnailQuality);
        var avatar = Render(image, origin, square, avatarSide, avatarSide, AvatarQuality);
        return full is null || thumbnail is null || avatar is null ? null : new ImageVariants(full, fullWidth, fullHeight, thumbnail, avatar);
    }

    /// <summary>The first frame, fully decoded; null unless every pixel decoded (a truncated file is rejected).</summary>
    private static SKBitmap? Decode(SKCodec codec)
    {
        var info = codec.Info.WithColorType(SKImageInfo.PlatformColorType).WithAlphaType(SKAlphaType.Premul);
        var bitmap = new SKBitmap();
        if (!bitmap.TryAllocPixels(info))
        {
            bitmap.Dispose();
            return null;
        }

        var result = codec.GetPixels(info, bitmap.GetPixels(), new SKCodecOptions(0));
        if (result != SKCodecResult.Success)
        {
            bitmap.Dispose();
            return null;
        }

        return bitmap;
    }

    /// <summary>
    /// Draws <paramref name="crop"/> (in upright coordinates) scaled to the target size: the orientation, the
    /// crop and the scaling are one transform, so no full-size intermediate copy is ever made.
    /// </summary>
    private static byte[]? Render(SKImage image, SKEncodedOrigin origin, SKRect crop, int width, int height, float quality)
    {
        var info = new SKImageInfo(width, height, SKImageInfo.PlatformColorType, SKAlphaType.Premul, SKColorSpace.CreateSrgb());
        using var surface = SKSurface.Create(info);
        if (surface is null)
        {
            return null;
        }

        var canvas = surface.Canvas;
        canvas.Clear(SKColors.Transparent);
        canvas.Scale(width / crop.Width, height / crop.Height);
        canvas.Translate(-crop.Left, -crop.Top);
        var orientation = OrientationMatrix(origin, image.Width, image.Height);
        canvas.Concat(in orientation);
        canvas.DrawImage(image, 0, 0, Sampling, null);
        canvas.Flush();

        using var snapshot = surface.Snapshot();
        using var pixels = snapshot.PeekPixels();
        using var encoded = pixels.Encode(new SKWebpEncoderOptions(SKWebpEncoderCompression.Lossy, quality));
        return encoded?.ToArray();
    }

    private static (int Width, int Height) Upright(int width, int height, SKEncodedOrigin origin) =>
        origin >= SKEncodedOrigin.LeftTop ? (height, width) : (width, height);

    /// <summary>
    /// Maps the stored pixels (<paramref name="width"/> × <paramref name="height"/>) to how the EXIF
    /// orientation says they are meant to be seen.
    /// </summary>
    internal static SKMatrix OrientationMatrix(SKEncodedOrigin origin, int width, int height) => origin switch
    {
        SKEncodedOrigin.TopRight => new SKMatrix(-1, 0, width, 0, 1, 0, 0, 0, 1),
        SKEncodedOrigin.BottomRight => new SKMatrix(-1, 0, width, 0, -1, height, 0, 0, 1),
        SKEncodedOrigin.BottomLeft => new SKMatrix(1, 0, 0, 0, -1, height, 0, 0, 1),
        SKEncodedOrigin.LeftTop => new SKMatrix(0, 1, 0, 1, 0, 0, 0, 0, 1),
        SKEncodedOrigin.RightTop => new SKMatrix(0, -1, height, 1, 0, 0, 0, 0, 1),
        SKEncodedOrigin.RightBottom => new SKMatrix(0, -1, height, -1, 0, width, 0, 0, 1),
        SKEncodedOrigin.LeftBottom => new SKMatrix(0, 1, 0, -1, 0, width, 0, 0, 1),
        _ => SKMatrix.Identity,
    };

    private static ImageFormat? FormatOf(SKEncodedImageFormat format) => format switch
    {
        SKEncodedImageFormat.Jpeg => ImageFormat.Jpeg,
        SKEncodedImageFormat.Png => ImageFormat.Png,
        SKEncodedImageFormat.Gif => ImageFormat.Gif,
        SKEncodedImageFormat.Webp => ImageFormat.WebP,
        _ => null,
    };
}
