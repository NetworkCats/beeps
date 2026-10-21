using Beeps.Features.Beeps;
using Beeps.Features.Shared;

namespace Beeps.Features.Notifications;

/// <summary>Stored as snake_case text; published API values must never change.</summary>
public enum NotificationType
{
    Mention,
    Reply,
    Like,
}

/// <summary>
/// One entry in an actor's inbox. At most one per recipient, type, actor and beep, which makes the
/// generating jobs idempotent and stops like/unlike/like from notifying twice. The inbox shows those of the same type
/// about the same beep (likes of one beep, in practice) as one entry, the newest, which a new one brings back to the top.
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

    /// <summary>
    /// A newer one of the same type about the same beep exists, so this one is not an inbox entry's head; the inbox walks
    /// only heads, however many likes a beep collects. Set when the newer one is added.
    /// </summary>
    public bool Superseded { get; init; }
}

/// <summary>Who a new beep concerns, for notifications.</summary>
public sealed record BeepAudience(Guid AuthorId, Guid? ReplyToAuthorId, IReadOnlyList<Guid> MentionedIds);

/// <summary>
/// An inbox entry: the newest notification of its type about its beep that the recipient can still see, from an actor
/// that is still visible, standing for the <paramref name="Others"/> older ones like it.
/// </summary>
/// <param name="ReadAt">Null when any of them is unread: they are only ever marked read up to an id or by their beep.</param>
/// <param name="Earlier">The actors of the newest of the others, newest first, at most <see cref="EarlierShown"/>.</param>
public sealed record NotificationRow(
    Guid Id, NotificationType Type, ActorRow From, Guid BeepId, DateTimeOffset CreatedAt, DateTimeOffset? ReadAt, int Others = 0, IReadOnlyList<ActorRow>? Earlier = null)
{
    /// <summary>The inbox shows the faces of this many of those who liked a beep before the newest.</summary>
    public const int EarlierShown = 5;
}

/// <param name="Others">How many more actors did the same (liked the same beep), before <paramref name="From"/>.</param>
/// <param name="Earlier">The newest of those others.</param>
public sealed record NotificationView(
    Guid Id, NotificationType Type, ActorView From, BeepView Beep, DateTimeOffset CreatedAt, bool Read, int Others = 0, IReadOnlyList<ActorView>? Earlier = null);

public interface INotificationStore
{
    /// <summary>Null if the beep no longer exists or was deleted.</summary>
    Task<BeepAudience?> GetBeepAudienceAsync(Guid beepId, CancellationToken cancellationToken);

    /// <summary>The author of the liked beep, or null if the like was taken back or the beep deleted since.</summary>
    Task<Guid?> GetLikedBeepAuthorAsync(Guid beepId, Guid actorId, CancellationToken cancellationToken);

    /// <summary>Inserts the notifications, skipping any that already exist. Runs immediately.</summary>
    Task AddAsync(IReadOnlyCollection<Notification> notifications, CancellationToken cancellationToken);

    /// <summary>Newest first, as entries (<see cref="NotificationRow"/>); only notifications about beeps the recipient can still see.</summary>
    Task<CursorPage<NotificationRow>> ListAsync(Guid recipientId, PageRequest page, bool unreadOnly, CancellationToken cancellationToken);

    /// <summary>Unread entries, as <see cref="ListAsync"/> lists them; with <paramref name="max"/>, counting stops there.</summary>
    Task<int> CountUnreadAsync(Guid recipientId, int? max, CancellationToken cancellationToken);

    /// <summary>
    /// Who has unread notifications about these beeps or from these actors, whatever they may see now: their unread
    /// count may have changed when the beeps or actors were hidden or came back. At most <paramref name="max"/>.
    /// </summary>
    Task<IReadOnlyList<Guid>> ListUnreadRecipientsAsync(IReadOnlyCollection<Guid> beepIds, IReadOnlyCollection<Guid> fromActorIds, int max, CancellationToken cancellationToken);

    /// <summary>Marks unread notifications read, all of them or those up to and including <paramref name="upToId"/>. Runs immediately; returns how many.</summary>
    Task<int> MarkReadAsync(Guid recipientId, Guid? upToId, DateTimeOffset now, CancellationToken cancellationToken);

    /// <summary>
    /// Marks read what the recipient has now seen: the mentions and replies that are <paramref name="seenBeepIds"/>, and
    /// every notification about <paramref name="openedBeepId"/>, the beep whose page they opened. Runs immediately;
    /// returns how many.
    /// </summary>
    Task<int> MarkSeenAsync(Guid recipientId, IReadOnlyCollection<Guid> seenBeepIds, Guid? openedBeepId, DateTimeOffset now, CancellationToken cancellationToken);
}
