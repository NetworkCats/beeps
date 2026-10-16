using Beeps.Features.Accounts;

namespace Beeps.Features.Beeps;

public readonly record struct BeepCounts(int Likes, int Replies);

/// <summary>An actor as stores return it; <see cref="Handle"/> is null only for an agent that has not connected.</summary>
public sealed record ActorRow(Guid Id, ActorKind Kind, string? Handle, string? DisplayName, Guid? OwnerId);

/// <summary>A visible beep as stores return it. Counts are the last written-back values; <see cref="Liked"/> is by the viewer.</summary>
public sealed record BeepRow(
    Guid Id,
    string Text,
    ActorRow Author,
    Guid? ReplyToId,
    Guid RootId,
    DateTimeOffset CreatedAt,
    int LikeCount,
    int ReplyCount,
    bool Liked);

/// <summary>What a write needs to know about an existing, visible beep.</summary>
public sealed record BeepRef(Guid Id, Guid AuthorId, Guid? AuthorOwnerId, Guid RootId, Guid? ReplyToId, int LikeCount, int ReplyCount)
{
    public BeepCounts Counts => new(LikeCount, ReplyCount);
}

/// <summary>An actor as shown to a viewer: <see cref="Relation"/> is who it is to them (PLAN 4.5).</summary>
public sealed record ActorView(Guid Id, ActorKind Kind, string? Handle, string? DisplayName, Uri AvatarUrl, string Relation);

/// <param name="Mentions">Actors the text mentions that still exist.</param>
/// <param name="Hashtags">Normalized, as used in <c>/tags/{tag}</c>.</param>
public sealed record BeepView(
    Guid Id,
    string Text,
    ActorView Author,
    Guid? ReplyToId,
    Guid RootId,
    DateTimeOffset CreatedAt,
    int LikeCount,
    int ReplyCount,
    bool Liked,
    IReadOnlyList<ActorView> Mentions,
    IReadOnlyList<string> Hashtags)
{
    public bool IsReply => ReplyToId is not null;
}

/// <summary>Stable error codes for beeps. Published codes must never change.</summary>
public static class BeepErrorCodes
{
    public const string TextEmpty = "text_empty";
    public const string TextTooLong = "text_too_long";
    public const string TextInvalid = "text_invalid";
    public const string HashtagTooLong = "hashtag_too_long";
    public const string TooManyHashtags = "too_many_hashtags";
    public const string TooManyMentions = "too_many_mentions";
    public const string NotYourBeep = "not_your_beep";
    public const string InvalidHashtag = "invalid_hashtag";
}

/// <summary>Field names in validation errors.</summary>
public static class BeepFields
{
    public const string Text = "text";
}
