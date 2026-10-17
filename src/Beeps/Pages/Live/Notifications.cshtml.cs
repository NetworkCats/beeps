using Beeps.Features.Auth;
using Beeps.Features.Live;
using Beeps.Features.Notifications;
using Beeps.Features.Shared;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Beeps.Pages.Live;

/// <summary>
/// What the inbox page fetches when its inbox changes: the entries newer than the newest it shows, or, without
/// <see cref="After"/>, the newest <see cref="Limit"/> entries, to bring the whole page up to date (read in
/// another tab, gone with their beep).
/// </summary>
public sealed class NotificationsModel(NotificationService notifications) : PageModel
{
    [BindProperty(SupportsGet = true)] public Guid? After { get; set; }

    [BindProperty(SupportsGet = true)] public int? Limit { get; set; }

    public IReadOnlyList<NotificationView> Notifications { get; private set; } = [];

    public async Task OnGetAsync(CancellationToken cancellationToken) =>
        Notifications = (await notifications.ListAsync(User.Viewer(), PageRequest.Create(sinceId: After, limit: Limit ?? LiveFeedService.Limit), unreadOnly: false, cancellationToken)).Items;
}
