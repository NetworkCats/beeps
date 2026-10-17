using Beeps.Features.Beeps;
using Beeps.Features.Media;
using Beeps.Features.Shared;

namespace Beeps.Pages;

/// <summary>One beep for <c>_Beep.cshtml</c>.</summary>
/// <param name="Focused">The beep the page is about, within its thread.</param>
/// <param name="InThread">Shown inside its thread, so a reply needs no link to where it belongs.</param>
/// <param name="ReplyingTo">In a thread: the handle of the author it answers, when that beep is on the page.</param>
public sealed record BeepCard(BeepView Beep, bool Focused = false, bool InThread = false, string? ReplyingTo = null);

/// <summary>A page of beeps for <c>_BeepList.cshtml</c>, with an empty state and a link to the next page.</summary>
/// <param name="CursorName">The query parameter the next page is asked for with: <c>before</c>, or <c>after</c> for threads.</param>
/// <param name="PagerPath">Where older pages are asked for; the page's own address when null.</param>
public sealed record BeepList(CursorPage<BeepView> Page, string EmptyTitle, string? EmptyText = null, string CursorName = "before", string? PagerPath = null);

/// <summary>
/// The square picked in the avatar cropper, posted as the hidden fields <c>Crop.X</c>, <c>Crop.Y</c> and
/// <c>Crop.Size</c>. Without script they stay empty and the centered square is used.
/// </summary>
public sealed class AvatarCropForm
{
    public int? X { get; set; }

    public int? Y { get; set; }

    public int? Size { get; set; }

    public AvatarCrop? ToCrop() => X is { } x && Y is { } y && Size is { } size ? new AvatarCrop(x, y, size) : null;
}
