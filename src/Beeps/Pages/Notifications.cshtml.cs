using Beeps.Features.Auth;
using Beeps.Features.Live;
using Beeps.Features.Notifications;
using Beeps.Features.Shared;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Beeps.Pages;

/// <summary>The inbox (PLAN 4.3): mentions, replies and likes, newest first.</summary>
public sealed class NotificationsModel(NotificationService notifications) : PageModel
{
    [BindProperty(SupportsGet = true)] public Guid? Before { get; set; }

    public CursorPage<NotificationView> Notifications { get; private set; } = new([], null);

    public LiveTopic Live => Before is null ? LiveTopic.Notifications : LiveTopic.None;

    public async Task OnGetAsync(CancellationToken cancellationToken) =>
        Notifications = await notifications.ListAsync(User.Viewer(), PageRequest.Create(Before), unreadOnly: false, cancellationToken);

    /// <param name="upToId">The newest notification on the page, so ones that arrived since stay unread.</param>
    public async Task<IActionResult> OnPostReadAsync(Guid? upToId, CancellationToken cancellationToken)
    {
        await notifications.MarkReadAsync(User.Viewer(), upToId, cancellationToken);
        return RedirectToPage();
    }
}
