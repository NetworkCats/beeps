using Beeps.Features.Auth;
using Beeps.Features.Live;
using Beeps.Features.Notifications;
using Beeps.Features.Shared;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Beeps.Pages.Live;

/// <summary>What the inbox page fetches when its unread count goes up: the entries newer than the newest it shows.</summary>
public sealed class NotificationsModel(NotificationService notifications) : PageModel
{
    [BindProperty(SupportsGet = true)] public Guid? After { get; set; }

    public IReadOnlyList<NotificationView> Notifications { get; private set; } = [];

    public async Task OnGetAsync(CancellationToken cancellationToken) =>
        Notifications = (await notifications.ListAsync(User.Viewer(), PageRequest.Create(sinceId: After, limit: LiveFeedService.Limit), unreadOnly: false, cancellationToken)).Items;
}
