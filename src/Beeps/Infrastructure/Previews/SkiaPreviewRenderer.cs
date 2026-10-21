using Beeps.Features.Avatars;
using Beeps.Features.Previews;
using Jdenticon.Rendering;
using SkiaSharp;

namespace Beeps.Infrastructure.Previews;

/// <summary>
/// SkiaSharp implementation of <see cref="IPreviewRenderer"/>, in the site's light theme (Client/scss/_theme.scss): one
/// flat white page, ink and one secondary gray, hairlines, and the indigo accent only where the page has it. Each card
/// ends in the logo and the site's name, as a link to it.
/// </summary>
public sealed class SkiaPreviewRenderer : IPreviewRenderer, IDisposable
{
    private const int Width = IPreviewRenderer.Width;
    private const int Height = IPreviewRenderer.Height;
    private const float Pad = 72;

    // The light theme's tokens, worked out from their oklch: --cui-bg, --cui-text, --cui-text-2, --cui-surface-2,
    // --cui-surface-3 (an empty banner), --cui-border, --cui-primary-text (mentions, hashtags and links) and --cui-avatar-bg.
    private static readonly SKColor Page = SKColors.White;
    private static readonly SKColor Ink = new(0x12, 0x12, 0x12);
    private static readonly SKColor Muted = new(0x5b, 0x5b, 0x5b);
    private static readonly SKColor Surface2 = new(0xf5, 0xf5, 0xf5);
    private static readonly SKColor Surface3 = new(0xe5, 0xe5, 0xe5);
    private static readonly SKColor Hairline = new(0xeb, 0xeb, 0xeb);
    private static readonly SKColor Accent = new(0x42, 0x49, 0xb0);
    private static readonly SKColor AvatarBackground = new(0xe7, 0xeb, 0xfb);

    private static readonly SKSamplingOptions Sampling = new(SKFilterMode.Linear, SKMipmapMode.Linear);

    private readonly PreviewFonts fonts;
    private readonly SKImage logo;
    private readonly SKImage art;

    public SkiaPreviewRenderer(PreviewFonts fonts, IWebHostEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(environment);
        this.fonts = fonts;
        logo = Asset(environment, "images/logo.png");
        art = Asset(environment, "images/chill.png");
    }

    /// <summary>
    /// The author over their beep, as in the thread, its text as large as it fits; its first image, if any, beside it.
    /// Below, the site and the day it was posted.
    /// </summary>
    public byte[] Render(BeepPreview beep)
    {
        ArgumentNullException.ThrowIfNull(beep);
        return Draw(canvas =>
        {
            using var image = Decode(beep.Image);
            const float imageWidth = 420;
            const float gap = 48;
            var right = image is null ? Width - Pad : Width - Pad - imageWidth - gap;
            if (image is not null)
            {
                DrawCover(canvas, image, SKRect.Create(Width - Pad - imageWidth, 64, imageWidth, Height - 128), 24, border: true);
            }

            const float avatar = 88;
            DrawAvatar(canvas, beep.Author, Pad, 64, avatar);
            DrawNameAndHandle(canvas, beep.Author, Pad + avatar + 24, 64, avatar, right);

            var spans = beep.Text.Select(s => new TextSpan(s.Text, s.Accent ? Accent : Ink)).ToList();
            const float textTop = 64 + avatar + 36;
            const float footerTop = Height - 64 - 48;
            var (text, lineHeight) = Fit(spans, right - Pad, footerTop - 24 - textTop, [56, 48, 42, 36, 32]);
            text.Draw(canvas, Pad, textTop, lineHeight);

            var brandWidth = DrawBrand(canvas, Pad, footerTop, 48);
            var date = TextLayout.Create(fonts, [new TextSpan(beep.Date, Muted)], bold: false, 28, right - Pad - brandWidth - 32, 1);
            date.Draw(canvas, right - date.Width, footerTop, 48);
        });
    }

    /// <summary>The top of the profile page: the banner, the avatar over its edge, the name, handle and bio.</summary>
    public byte[] Render(ProfilePreview profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        return Draw(canvas =>
        {
            const float bannerHeight = 250;
            var banner = SKRect.Create(0, 0, Width, bannerHeight);
            using (var image = Decode(profile.Banner))
            {
                if (image is null)
                {
                    using var fill = new SKPaint { Color = Surface3 };
                    canvas.DrawRect(banner, fill);
                }
                else
                {
                    DrawCover(canvas, image, banner, 0, border: false);
                }
            }

            const float avatar = 168;
            const float ring = 8;
            using (var paint = new SKPaint { Color = Page, IsAntialias = true })
            {
                canvas.DrawCircle(Pad + (avatar / 2), bannerHeight, (avatar / 2) + ring, paint);
            }

            DrawAvatar(canvas, profile.Actor, Pad, bannerHeight - (avatar / 2), avatar);
            // Where the profile page has its follow button.
            DrawBrand(canvas, Width - Pad, bannerHeight + 24, 52, alignRight: true);

            var top = bannerHeight + (avatar / 2) + 28;
            var badgeWidth = profile.Actor.Badge is { } label ? Badge(label, 30).Width + 16 : 0;
            var name = TextLayout.Create(fonts, [new TextSpan(profile.Actor.Name, Ink)], bold: true, 52, Width - (2 * Pad) - badgeWidth, 1);
            name.Draw(canvas, Pad, top, 66);
            if (profile.Actor.Badge is { } badge)
            {
                DrawBadge(canvas, badge, 30, Pad + name.Width + 16, top + 33);
            }

            top += 66;
            if (profile.Actor.Handle is { } handle)
            {
                var at = TextLayout.Create(fonts, [new TextSpan("@" + handle, Muted)], bold: false, 34, Width - (2 * Pad), 1);
                at.Draw(canvas, Pad, top, 46);
                top += 46 + 14;
            }

            if (!string.IsNullOrWhiteSpace(profile.Bio))
            {
                var bio = TextLayout.Create(fonts, [new TextSpan(profile.Bio, Ink)], bold: false, 32, Width - (2 * Pad), (int)((Height - 48 - top) / 44));
                bio.Draw(canvas, Pad, top, 44);
            }
        });
    }

    /// <summary>The home page's welcome: the site's name and what it is, beside the robot it shows.</summary>
    public byte[] Render(SitePreview site)
    {
        ArgumentNullException.ThrowIfNull(site);
        return Draw(canvas =>
        {
            // chill.png has empty space around the robot, more below than above (as .intro-art says): centred on the robot.
            const float artSize = 470;
            var artTop = ((Height - (artSize * (534 - 46 - 82) / 534)) / 2) - (artSize * 46 / 534);
            canvas.DrawImage(art, SKRect.Create(Width - Pad - artSize + 24, artTop, artSize, artSize), Sampling);

            var column = Width - (2 * Pad) - artSize;
            const float logoSize = 104;
            const float nameHeight = 120;
            const float taglineHeight = 52;
            var name = TextLayout.Create(fonts, [new TextSpan(site.Name, Ink)], bold: true, 100, column, 1);
            var tagline = TextLayout.Create(fonts, [new TextSpan(site.Tagline, Muted)], bold: false, 36, column, 3);
            var top = (Height - (logoSize + 16 + nameHeight + (taglineHeight * tagline.LineCount))) / 2;
            // The logo's own canvas has a margin: pulled out by as much, so its pager lines up with the text.
            canvas.DrawImage(logo, SKRect.Create(Pad - (logoSize * 14 / 534), top, logoSize, logoSize), Sampling);
            top += logoSize + 16;
            name.Draw(canvas, Pad, top, nameHeight);
            tagline.Draw(canvas, Pad, top + nameHeight, taglineHeight);
        });
    }

    public void Dispose()
    {
        logo.Dispose();
        art.Dispose();
    }

    private static byte[] Draw(Action<SKCanvas> draw)
    {
        using var surface = SKSurface.Create(new SKImageInfo(Width, Height, SKColorType.Rgba8888, SKAlphaType.Premul));
        surface.Canvas.Clear(Page);
        draw(surface.Canvas);
        using var snapshot = surface.Snapshot();
        using var png = snapshot.Encode(SKEncodedImageFormat.Png, 100);
        return png.ToArray();
    }

    private static SKImage Asset(IWebHostEnvironment environment, string path)
    {
        using var stream = environment.WebRootFileProvider.GetFileInfo(path).CreateReadStream();
        using var data = SKData.Create(stream);
        return SKImage.FromEncodedData(data) ?? throw new InvalidOperationException($"wwwroot/{path} is not an image.");
    }

    /// <summary>A stored image (WebP); null for none, or one that does not decode, which is then left out.</summary>
    private static SKImage? Decode(byte[]? data) => data is null ? null : SKImage.FromEncodedData(data);

    /// <summary>The largest of <paramref name="sizes"/> the whole text fits in; failing all, the smallest, cut short.</summary>
    private (TextLayout Layout, float LineHeight) Fit(List<TextSpan> spans, float width, float height, float[] sizes)
    {
        foreach (var size in sizes)
        {
            var lineHeight = size * 1.35f;
            var layout = TextLayout.Create(fonts, spans, bold: false, size, width, Math.Max(1, (int)(height / lineHeight)));
            if (!layout.Truncated || size == sizes[^1])
            {
                return (layout, lineHeight);
            }
        }

        throw new ArgumentException("No sizes to fit the text in.", nameof(sizes));
    }

    private void DrawNameAndHandle(SKCanvas canvas, PreviewActor actor, float x, float top, float height, float right)
    {
        const float nameSize = 38;
        const float badgeSize = 24;
        var badgeWidth = actor.Badge is { } label ? Badge(label, badgeSize).Width + 12 : 0;
        var lineHeight = height / 2;
        var name = TextLayout.Create(fonts, [new TextSpan(actor.Name, Ink)], bold: true, nameSize, right - x - badgeWidth, 1);
        name.Draw(canvas, x, top, lineHeight);
        if (actor.Badge is { } badge)
        {
            DrawBadge(canvas, badge, badgeSize, x + name.Width + 12, top + (lineHeight / 2));
        }

        if (actor.Handle is { } handle)
        {
            var at = TextLayout.Create(fonts, [new TextSpan("@" + handle, Muted)], bold: false, 32, right - x, 1);
            at.Draw(canvas, x, top + lineHeight, lineHeight);
        }
    }

    /// <summary>A label's size: as on the page, a soft pill a little taller than its text.</summary>
    private SKSize Badge(string label, float size)
    {
        var text = TextLayout.Create(fonts, [new TextSpan(label, Muted)], bold: true, size, Width, 1);
        return new SKSize(text.Width + size, size * 1.6f);
    }

    /// <summary>A label centred on <paramref name="middle"/>.</summary>
    private void DrawBadge(SKCanvas canvas, string label, float size, float x, float middle)
    {
        var box = Badge(label, size);
        var rect = SKRect.Create(x, middle - (box.Height / 2), box.Width, box.Height);
        using (var fill = new SKPaint { Color = Surface2, IsAntialias = true })
        {
            canvas.DrawRoundRect(rect, size * 0.3f, size * 0.3f, fill);
        }

        var text = TextLayout.Create(fonts, [new TextSpan(label, Muted)], bold: true, size, Width, 1);
        text.Draw(canvas, x + (size / 2), rect.Top, rect.Height);
    }

    /// <summary>The logo and the site's name, in a row of <paramref name="height"/>; how wide it is.</summary>
    private float DrawBrand(SKCanvas canvas, float x, float top, float height, bool alignRight = false)
    {
        var name = TextLayout.Create(fonts, [new TextSpan("beeps", Ink)], bold: true, height * 0.66f, Width, 1);
        var gap = height * 0.2f;
        var width = height + gap + name.Width;
        var left = alignRight ? x - width : x;
        canvas.DrawImage(logo, SKRect.Create(left, top, height, height), Sampling);
        name.Draw(canvas, left + height + gap, top, height);
        return width;
    }

    private static void DrawAvatar(SKCanvas canvas, PreviewActor actor, float x, float y, float size)
    {
        var circle = SKRect.Create(x, y, size, size);
        using var image = Decode(actor.Avatar);
        canvas.Save();
        canvas.ClipRoundRect(new SKRoundRect(circle, size / 2), antialias: true);

        using (var fill = new SKPaint { Color = AvatarBackground })
        {
            canvas.DrawRect(circle, fill);
        }

        if (image is null)
        {
            // The default avatar's pattern (DefaultAvatar), drawn rather than read from its SVG.
            canvas.Translate(x, y);
            using var identicon = new CanvasIdenticon(canvas);
            DefaultAvatar.Icon(actor.Id, (int)size).Draw(identicon);
        }
        else
        {
            DrawCover(canvas, image, circle, 0, border: false);
        }

        canvas.Restore();
    }

    /// <summary>The image cropped to fill <paramref name="rect"/> about its centre, as CSS's <c>object-fit: cover</c>.</summary>
    private static void DrawCover(SKCanvas canvas, SKImage image, SKRect rect, float radius, bool border)
    {
        var scale = Math.Max(rect.Width / image.Width, rect.Height / image.Height);
        var (width, height) = (rect.Width / scale, rect.Height / scale);
        var source = SKRect.Create((image.Width - width) / 2, (image.Height - height) / 2, width, height);
        canvas.Save();
        canvas.ClipRoundRect(new SKRoundRect(rect, radius), antialias: true);
        canvas.DrawImage(image, source, rect, Sampling);
        canvas.Restore();
        if (border)
        {
            using var line = new SKPaint { Color = Hairline, IsAntialias = true, IsStroke = true, StrokeWidth = 2 };
            rect.Inflate(-1, -1);
            canvas.DrawRoundRect(rect, radius - 1, radius - 1, line);
        }
    }

    /// <summary>Jdenticon's shapes as Skia paths: one per colour, holes wound the other way.</summary>
    private sealed class CanvasIdenticon(SKCanvas canvas) : Renderer, IDisposable
    {
        private readonly SKPathBuilder path = new() { FillType = SKPathFillType.Winding };
        private SKColor color;

        public override void SetBackground(Jdenticon.Rendering.Color color, Rectangle rect)
        {
            // Transparent in the style DefaultAvatar uses: the avatar's own background shows.
        }

        public override IDisposable BeginShape(Jdenticon.Rendering.Color color)
        {
            path.Reset();
            this.color = new SKColor((byte)color.R, (byte)color.G, (byte)color.B, (byte)color.A);
            return new Shape(this);
        }

        public void Dispose() => path.Dispose();

        protected override void AddPolygonNoTransform(PointF[] points) =>
            path.AddPoly([.. points.Select(p => new SKPoint(p.X, p.Y))], close: true);

        protected override void AddCircleNoTransform(PointF location, float diameter, bool counterClockwise)
        {
            var radius = diameter / 2;
            path.AddCircle(location.X + radius, location.Y + radius, radius, counterClockwise ? SKPathDirection.CounterClockwise : SKPathDirection.Clockwise);
        }

        private void EndShape()
        {
            using var shape = path.Detach();
            using var paint = new SKPaint { Color = color, IsAntialias = true };
            canvas.DrawPath(shape, paint);
        }

        private sealed class Shape(CanvasIdenticon renderer) : IDisposable
        {
            public void Dispose() => renderer.EndShape();
        }
    }
}
