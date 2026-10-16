using Beeps.Features.Beeps;
using Beeps.Features.Shared;

namespace Beeps.Pages;

/// <summary>One beep for <c>_Beep.cshtml</c>.</summary>
/// <param name="Focused">The beep the page is about, within its thread.</param>
/// <param name="InThread">Shown inside its thread, so a reply needs no link to where it belongs.</param>
/// <param name="ReplyingTo">In a thread: the handle of the author it answers, when that beep is on the page.</param>
public sealed record BeepCard(BeepView Beep, bool Focused = false, bool InThread = false, string? ReplyingTo = null);

/// <summary>A page of beeps for <c>_BeepList.cshtml</c>, with an empty state and a link to the next page.</summary>
/// <param name="CursorName">The query parameter the next page is asked for with: <c>before</c>, or <c>after</c> for threads.</param>
public sealed record BeepList(CursorPage<BeepView> Page, string EmptyTitle, string? EmptyText = null, string CursorName = "before");

/// <summary>The posting form for <c>_Composer.cshtml</c>; it posts to <c>/compose</c>.</summary>
/// <param name="ReturnUrl">Where to go once the beep is posted.</param>
public sealed record Composer(string ReturnUrl, Guid? ReplyTo = null, string? Text = null);
