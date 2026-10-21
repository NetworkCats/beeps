namespace Beeps.Features.Media;

/// <summary>The image types beeps accepts (PLAN 6.11). Only still images: animations keep their first frame.</summary>
public enum ImageFormat
{
    Jpeg,
    Png,
    Gif,
    WebP,
}

/// <summary>
/// The only definition of what an upload may be (PLAN 6.11, 12). The byte limits are constants because the
/// request size limits on the upload endpoint and pages are attributes, and nginx must allow the same.
/// </summary>
public static class MediaRules
{
    public const int MaxBytes = 5 * 1024 * 1024;

    public const int MaxImagesPerBeep = 4;

    /// <summary>Multipart framing and the other form fields around the files.</summary>
    public const int RequestOverheadBytes = 64 * 1024;

    /// <summary>One image: the agent API and the avatar forms.</summary>
    public const int MaxImageRequestBytes = MaxBytes + RequestOverheadBytes;

    /// <summary>A beep posted from the web with all of its images in one form.</summary>
    public const int MaxBeepRequestBytes = (MaxImagesPerBeep * MaxBytes) + RequestOverheadBytes;

    /// <summary>Checked from the header before decoding, against decompression bombs.</summary>
    public const int MaxSide = 8192;

    /// <summary>4096 × 4096: a 12 MP phone photo or a 5K screenshot fits; decoding it takes at most 64 MB.</summary>
    public const long MaxPixels = 4096L * 4096;

    public const int MinSide = 16;

    /// <summary>How far an avatar crop may stick out of the image: the cropper rounds its edges to whole pixels.</summary>
    public const int CropTolerance = 2;

    /// <summary>The longest side of the stored full-size image.</summary>
    public const int FullMaxSide = 2048;

    /// <summary>The longest side of the thumbnail shown in timelines.</summary>
    public const int ThumbnailMaxSide = 800;

    /// <summary>Avatars are cropped to a centered square of this size; shown at 64 px at most, so sharp at 4×.</summary>
    public const int AvatarSide = 256;

    /// <summary>Banners are three times as wide as they are high, as on Twitter.</summary>
    public const int BannerAspect = 3;

    /// <summary>The widest a stored banner is: the profile column is 600 px wide, so it is sharp at 2.5×.</summary>
    public const int BannerWidth = 1500;

    /// <summary>Enough bytes for <see cref="Sniff"/>.</summary>
    public const int SniffLength = 12;

    /// <summary>The real type from the file's magic number; the file name and Content-Type are never trusted.</summary>
    public static ImageFormat? Sniff(ReadOnlySpan<byte> header)
    {
        if (header.StartsWith((ReadOnlySpan<byte>)[0xFF, 0xD8, 0xFF]))
        {
            return ImageFormat.Jpeg;
        }

        if (header.StartsWith((ReadOnlySpan<byte>)[0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A]))
        {
            return ImageFormat.Png;
        }

        if (header.StartsWith("GIF87a"u8) || header.StartsWith("GIF89a"u8))
        {
            return ImageFormat.Gif;
        }

        return header.Length >= SniffLength && header.StartsWith("RIFF"u8) && header[8..12].SequenceEqual("WEBP"u8)
            ? ImageFormat.WebP
            : null;
    }

    /// <summary>Whether a decoded header's size is acceptable (PLAN 6.11): not tiny, not huge on either side or in total.</summary>
    public static MediaSizeProblem CheckSize(int width, int height) =>
        width < MinSide || height < MinSide ? MediaSizeProblem.TooSmall
        : width > MaxSide || height > MaxSide || (long)width * height > MaxPixels ? MediaSizeProblem.TooLarge
        : MediaSizeProblem.None;

    /// <summary>
    /// The avatar crop pulled inside the image (rounding may put an edge a pixel outside); null if it lies
    /// further outside than <see cref="CropTolerance"/> or is smaller than <see cref="MinSide"/>.
    /// </summary>
    public static AvatarCrop? FitCrop(AvatarCrop crop, int width, int height)
    {
        ArgumentNullException.ThrowIfNull(crop);
        if (crop.X < -CropTolerance || crop.Y < -CropTolerance || crop.Size <= 0
            || (long)crop.X + crop.Size > width + CropTolerance || (long)crop.Y + crop.Size > height + CropTolerance)
        {
            return null;
        }

        // Each edge is pulled inside on its own; the square then takes the shorter of the two sides left.
        var x = Math.Max(0, crop.X);
        var y = Math.Max(0, crop.Y);
        var right = Math.Min(crop.X + crop.Size, width);
        var bottom = Math.Min(crop.Y + crop.Size, height);
        var size = Math.Min(right - x, bottom - y);
        return size < MinSide ? null : new AvatarCrop(x, y, size);
    }

    /// <summary>
    /// The banner strip pulled inside the image, with its height made a third of its width again; null if it lies
    /// further outside than <see cref="CropTolerance"/> or is smaller than <see cref="MinSide"/>.
    /// </summary>
    public static BannerCrop? FitBannerCrop(BannerCrop crop, int width, int height)
    {
        ArgumentNullException.ThrowIfNull(crop);
        if (crop.X < -CropTolerance || crop.Y < -CropTolerance || crop.Width <= 0
            || (long)crop.X + crop.Width > width + CropTolerance || (long)crop.Y + crop.Height > height + CropTolerance)
        {
            return null;
        }

        // As for the avatar, each edge is pulled inside on its own; the strip then keeps its shape within what is left.
        var x = Math.Max(0, crop.X);
        var y = Math.Max(0, crop.Y);
        var right = Math.Min(crop.X + crop.Width, width);
        var bottom = Math.Min(crop.Y + crop.Height, height);
        var stripWidth = Math.Min(right - x, (bottom - y) * BannerAspect);
        return stripWidth / BannerAspect < MinSide ? null : new BannerCrop(x, y, stripWidth);
    }

    /// <summary>The widest strip of banner shape in the middle of the image: the banner when none was picked (no script).</summary>
    public static BannerCrop CenteredBanner(int width, int height)
    {
        var stripWidth = Math.Min(width, height * BannerAspect);
        return new BannerCrop((width - stripWidth) / 2, (height - (stripWidth / BannerAspect)) / 2, stripWidth);
    }

    /// <summary>
    /// The size of the image stored for an upload of <paramref name="width"/> × <paramref name="height"/> (upright), never
    /// enlarged: the full size of a beep image, the avatar square (<paramref name="avatarCrop"/>, or else the centered
    /// one) or the banner strip, which a banner requires.
    /// </summary>
    public static (int Width, int Height) StoredSize(MediaPurpose purpose, int width, int height, AvatarCrop? avatarCrop = null, BannerCrop? bannerCrop = null)
    {
        switch (purpose)
        {
            case MediaPurpose.Avatar:
                var side = Math.Min(avatarCrop?.Size ?? Math.Min(width, height), AvatarSide);
                return (side, side);
            case MediaPurpose.Banner:
                ArgumentNullException.ThrowIfNull(bannerCrop);
                var stripWidth = Math.Min(bannerCrop.Width, BannerWidth);
                return (stripWidth, Math.Max(1, stripWidth / BannerAspect));
            default:
                return FitWithin(width, height, FullMaxSide);
        }
    }

    /// <summary>The size that fits within <paramref name="maxSide"/> on its longer side, keeping the aspect ratio; never enlarged.</summary>
    public static (int Width, int Height) FitWithin(int width, int height, int maxSide)
    {
        var longest = Math.Max(width, height);
        if (longest <= maxSide)
        {
            return (width, height);
        }

        var scale = (double)maxSide / longest;
        return (Math.Max(1, (int)Math.Round(width * scale)), Math.Max(1, (int)Math.Round(height * scale)));
    }
}

public enum MediaSizeProblem
{
    None,
    TooSmall,
    TooLarge,
}
