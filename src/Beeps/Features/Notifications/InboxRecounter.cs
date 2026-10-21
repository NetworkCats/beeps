using Beeps.Features.Live;
using Beeps.Features.Shared;

namespace Beeps.Features.Notifications;

/// <summary>
/// After beeps or actors are hidden, deleted or come back, the notifications about them disappear or reappear with
/// them (the inbox only lists what its reader can see): the people with unread ones get their count again, live.
/// Best effort, like every live update: a failure is logged and never fails the change.
/// </summary>
public sealed partial class InboxRecounter(INotificationStore store, LiveUpdates live, ILogger<InboxRecounter> logger)
{
    /// <summary>Inboxes recounted at once; beyond this, the rest catch up on their next change.</summary>
    public const int MaxRecipients = 10_000;

    /// <summary>Recipients per inbox event.</summary>
    public const int BatchSize = 500;

    private static readonly LogThrottle RecountFailures = new();

    public async Task RecountAsync(IReadOnlyCollection<Guid> beepIds, IReadOnlyCollection<Guid> fromActorIds, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(beepIds);
        ArgumentNullException.ThrowIfNull(fromActorIds);
        if (beepIds.Count == 0 && fromActorIds.Count == 0)
        {
            return;
        }

        IReadOnlyList<Guid> recipients;
        try
        {
            recipients = await store.ListUnreadRecipientsAsync(beepIds, fromActorIds, MaxRecipients, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            if (RecountFailures.Allow())
            {
                LogRecountFailed(logger, exception);
            }
            return;
        }

        foreach (var batch in recipients.Chunk(BatchSize))
        {
            await live.InboxChangedAsync(batch, cancellationToken);
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Finding the inboxes to recount failed; their unread counts catch up on their next change (logged at most once a minute)")]
    private static partial void LogRecountFailed(ILogger logger, Exception exception);
}
