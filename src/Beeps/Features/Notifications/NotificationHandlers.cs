using Beeps.Features.Live;
using Beeps.Features.Shared;

namespace Beeps.Features.Notifications;

/// <summary>
/// Notifies the author of the beep a reply answers, and everyone a beep mentions. Nobody is notified about
/// their own beep, and a reply that also mentions the parent's author notifies them once, as a reply.
/// </summary>
public sealed class BeepNotificationsHandler(INotificationStore store, LiveUpdates live, TimeProvider time) : IOutboxHandler
{
    public string Type => BeepNotificationsRequest.MessageType;

    public async Task HandleAsync(OutboxEnvelope message, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);
        var request = message.ReadPayload(NotificationsJsonContext.Default.BeepNotificationsRequest);
        var audience = await store.GetBeepAudienceAsync(request.BeepId, cancellationToken);
        if (audience is null)
        {
            return;
        }

        // Created when handled, not when posted: ids then follow insertion order, which since_id relies on.
        var now = time.GetUtcNow();
        var notifications = new List<Notification>();
        var notified = new HashSet<Guid> { audience.AuthorId };
        if (audience.ReplyToAuthorId is { } parentAuthor && notified.Add(parentAuthor))
        {
            notifications.Add(New(parentAuthor, NotificationType.Reply, audience.AuthorId, request.BeepId, now));
        }

        foreach (var mentioned in audience.MentionedIds.Where(notified.Add))
        {
            notifications.Add(New(mentioned, NotificationType.Mention, audience.AuthorId, request.BeepId, now));
        }

        if (notifications.Count > 0)
        {
            await store.AddAsync(notifications, cancellationToken);
            await live.InboxChangedAsync([.. notifications.Select(n => n.RecipientId)], cancellationToken);
        }
    }

    internal static Notification New(Guid recipientId, NotificationType type, Guid fromActorId, Guid beepId, DateTimeOffset now) => new()
    {
        Id = Guid.CreateVersion7(now),
        RecipientId = recipientId,
        Type = type,
        FromActorId = fromActorId,
        BeepId = beepId,
        CreatedAt = now,
    };
}

/// <summary>Notifies an author that their beep was liked, unless the like was already taken back.</summary>
public sealed class LikeNotificationHandler(INotificationStore store, LiveUpdates live, TimeProvider time) : IOutboxHandler
{
    public string Type => LikeNotificationRequest.MessageType;

    public async Task HandleAsync(OutboxEnvelope message, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);
        var request = message.ReadPayload(NotificationsJsonContext.Default.LikeNotificationRequest);
        var author = await store.GetLikedBeepAuthorAsync(request.BeepId, request.ActorId, cancellationToken);
        if (author is not { } recipient || recipient == request.ActorId)
        {
            return;
        }

        await store.AddAsync(
            [BeepNotificationsHandler.New(recipient, NotificationType.Like, request.ActorId, request.BeepId, time.GetUtcNow())],
            cancellationToken);
        await live.InboxChangedAsync([recipient], cancellationToken);
    }
}
