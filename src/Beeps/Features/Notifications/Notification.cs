using Beeps.Features.Beeps;
using Beeps.Features.Shared;

namespace Beeps.Features.Notifications;

/// <summary>PLAN 4.3. Stored as snake_case text; published API values must never change.</summary>
public enum NotificationType
{
    Mention,
    Reply,
    Like,
}

/// <summary>
/// One entry in an actor's inbox. At most one per recipient, type, actor and beep, which makes the
/// generating jobs idempotent and stops like/unlike/like from notifying twice.
/// </summary>
public sealed class Notification
{
    public Guid Id { get; init; }

    public Guid RecipientId { get; init; }

    public NotificationType Type { get; init; }

    /// <summary>Who replied, mentioned or liked.</summary>
    public Guid FromActorId { get; init; }

    /// <summary>The reply, the mentioning beep, or the liked beep.</summary>
    public Guid BeepId { get; init; }

    public DateTimeOffset CreatedAt { get; init; }

    public DateTimeOffset? ReadAt { get; set; }
}

/// <summary>Who a new beep concerns, for notifications.</summary>
public sealed record BeepAudience(Guid AuthorId, Guid? ReplyToAuthorId, IReadOnlyList<Guid> MentionedIds);

/// <summary>A notification whose beep the recipient can still see, from an actor that is still visible.</summary>
public sealed record NotificationRow(Guid Id, NotificationType Type, ActorRow From, Guid BeepId, DateTimeOffset CreatedAt, DateTimeOffset? ReadAt);

public sealed record NotificationView(Guid Id, NotificationType Type, ActorView From, BeepView Beep, DateTimeOffset CreatedAt, bool Read);

public interface INotificationStore
{
    /// <summary>Null if the beep no longer exists or was deleted.</summary>
    Task<BeepAudience?> GetBeepAudienceAsync(Guid beepId, CancellationToken cancellationToken);

    /// <summary>The author of the liked beep, or null if the like was taken back or the beep deleted since.</summary>
    Task<Guid?> GetLikedBeepAuthorAsync(Guid beepId, Guid actorId, CancellationToken cancellationToken);

    /// <summary>Inserts the notifications, skipping any that already exist. Runs immediately.</summary>
    Task AddAsync(IReadOnlyCollection<Notification> notifications, CancellationToken cancellationToken);

    /// <summary>Newest first; only notifications about beeps the recipient can still see.</summary>
    Task<CursorPage<NotificationRow>> ListAsync(Guid recipientId, PageRequest page, bool unreadOnly, CancellationToken cancellationToken);

    /// <summary>Marks unread notifications read, all of them or those up to and including <paramref name="upToId"/>. Runs immediately; returns how many.</summary>
    Task<int> MarkReadAsync(Guid recipientId, Guid? upToId, DateTimeOffset now, CancellationToken cancellationToken);
}
