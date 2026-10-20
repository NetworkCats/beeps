using Beeps.Features.Beeps;
using Beeps.Features.Media;
using Beeps.Features.Shared;
using Beeps.Pages.Islands;

namespace Beeps.Pages;

/// <summary>The buttons for signing in or up with Google and GitHub, for <c>_ExternalLogins.cshtml</c>.</summary>
/// <param name="ReturnUrl">Where signing in leads once done, as the login page was asked for it.</param>
/// <param name="Passkey">The login page's passkey button, after theirs.</param>
public sealed record ExternalLoginLinks(string? ReturnUrl = null, PasskeySignInProps? Passkey = null);

/// <summary>One beep for <c>_Beep.cshtml</c>.</summary>
/// <param name="Focused">The beep the page is about, within its thread.</param>
/// <param name="InThread">Shown inside its thread, so a reply needs no link to where it belongs.</param>
/// <param name="ReplyingTo">In a thread: the handle of the author it answers, when that beep is on the page.</param>
public sealed record BeepCard(BeepView Beep, bool Focused = false, bool InThread = false, string? ReplyingTo = null);

/// <summary>A page of beeps for <c>_BeepList.cshtml</c>, with an empty state and a link to the next page.</summary>
/// <param name="PagerPath">Where older pages are asked for; the page's own address when null.</param>
public sealed record BeepList(CursorPage<BeepView> Page, string EmptyTitle, string? EmptyText = null, string? PagerPath = null)
{
    /// <summary>The address of the next, older page, from a page at <paramref name="path"/>; null on the last.</summary>
    public string? NextPath(string? path) => Page.NextCursor is { } next ? $"{PagerPath ?? path}?before={next}" : null;
}

/// <summary>
/// The square picked in the avatar cropper, posted as the hidden fields <c>Crop.X</c>, <c>Crop.Y</c> and
/// <c>Crop.Size</c>. Left empty when no square was picked, and the centered square is used.
/// </summary>
public sealed class AvatarCropForm
{
    public int? X { get; set; }

    public int? Y { get; set; }

    public int? Size { get; set; }

    public AvatarCrop? ToCrop() => X is { } x && Y is { } y && Size is { } size ? new AvatarCrop(x, y, size) : null;
}

/// <summary>
/// The strip picked in the banner cropper, posted as the hidden fields <c>BannerCrop.X</c>, <c>BannerCrop.Y</c> and
/// <c>BannerCrop.Width</c>; its height follows from the width. Left empty when no strip was picked, and the centered strip is used.
/// </summary>
public sealed class BannerCropForm
{
    public int? X { get; set; }

    public int? Y { get; set; }

    public int? Width { get; set; }

    public BannerCrop? ToCrop() => X is { } x && Y is { } y && Width is { } width ? new BannerCrop(x, y, width) : null;
}
