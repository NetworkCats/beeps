using System.Security.Claims;
using System.Text.Json.Serialization;
using Beeps.Features.Accounts;
using Beeps.Features.Auth;
using Beeps.Features.Beeps;
using Beeps.Features.Live;
using Beeps.Features.Notifications;
using Beeps.Features.Shared;

namespace Beeps.Pages.Islands;

// The props the islands are drawn from (Client/app/props.ts declares the same shapes). Built here, in the view layer,
// from what the Services return: the components only lay them out. Flags that are false and values that are null are
// left out of the JSON, which the components read as false and missing.

/// <summary>How an island follows the site live.</summary>
/// <param name="Watch">The page's <see cref="LiveTopic"/>: what new content it shows as it arrives.</param>
/// <param name="Subject">What the page is about (<c>beep:{id}</c>, <c>actor:{id}</c>): when that goes, the page is shown anew.</param>
/// <param name="Actors">People the island shows: when one of them changes, its props are fetched again.</param>
/// <param name="Url">Where its props are fetched again from, when the page's own address can change (a renamed profile).</param>
/// <param name="On">Other events after which its props are fetched again: <c>search</c>, <c>admin</c>, <c>inbox</c>, <see cref="Sessions"/>.</param>
public sealed record LiveProps(
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Watch = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Subject = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<Guid>? Actors = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Url = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? On = null)
{
    /// <summary>The account signed in or out somewhere (the devices page).</summary>
    public const string Sessions = "sessions";

    /// <summary>The topic, as members follow it; the props of someone not yet verified watch nothing.</summary>
    public static string? WatchFor(ClaimsPrincipal user, LiveTopic topic) =>
        user.IsMember() && topic.Kind != LiveTopicKind.None ? topic.ToString() : null;
}

public sealed record ActorProps(
    Guid Id,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] bool Agent,
    string? Handle,
    string? DisplayName,
    Uri Avatar)
{
    public static ActorProps From(ActorView actor)
    {
        ArgumentNullException.ThrowIfNull(actor);
        return new ActorProps(actor.Id, actor.Kind == ActorKind.Agent, actor.Handle, actor.DisplayName, actor.AvatarUrl);
    }
}

public sealed record MediaProps(Uri Url, Uri Thumbnail, int Width, int Height);

/// <summary>A beep, as <c>_Beep.cshtml</c> shows it.</summary>
/// <param name="Html">The text as <see cref="BeepText.RenderHtml(BeepView)"/> renders it: the one place mentions and hashtags become links.</param>
/// <param name="Mentions">Who it mentions: it is drawn again when one of them changes.</param>
/// <param name="Translation">The text in the viewer's language, shown in its place, with the original a click away.</param>
public sealed record BeepProps(
    Guid Id,
    ActorProps Author,
    DateTimeOffset CreatedAt,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] Guid? ReplyToId,
    string Html,
    IReadOnlyList<MediaProps> Media,
    int LikeCount,
    int ReplyCount,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] bool Liked,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] bool CanDelete,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] bool UnderReview,
    IReadOnlyList<Guid> Mentions,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] TranslationProps? Translation = null)
{
    public static BeepProps From(BeepView beep)
    {
        ArgumentNullException.ThrowIfNull(beep);
        return new BeepProps(
            beep.Id,
            ActorProps.From(beep.Author),
            beep.CreatedAt,
            beep.ReplyToId,
            BeepText.RenderHtml(beep),
            [.. beep.Media.Select(m => new MediaProps(m.Url, m.ThumbnailUrl, m.Width, m.Height))],
            beep.LikeCount,
            beep.ReplyCount,
            beep.Liked,
            beep.CanDelete,
            beep.UnderReview,
            [.. beep.Mentions.Select(m => m.Id)],
            beep.Translation is { } translation ? new TranslationProps(translation.Language, BeepText.RenderTranslationHtml(beep)!) : null);
    }
}

/// <summary>A beep's text in the viewer's language.</summary>
/// <param name="Lang">The language it was written in (an ISO 639 code), which the app names.</param>
/// <param name="Html">The translation, linked as <see cref="BeepProps.Html"/> is.</param>
public sealed record TranslationProps(string Lang, string Html);

/// <summary>A list of beeps, as <c>_BeepList.cshtml</c> shows it.</summary>
/// <param name="Next">The address of the next page; null on the last.</param>
public sealed record BeepListProps(
    IReadOnlyList<BeepProps> Items,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Next,
    string EmptyTitle,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? EmptyText)
{
    public static BeepListProps From(BeepList list, string path)
    {
        ArgumentNullException.ThrowIfNull(list);
        return new BeepListProps(
            [.. list.Page.Items.Select(BeepProps.From)],
            list.NextPath(path),
            list.EmptyTitle,
            list.EmptyText);
    }
}

/// <summary>One inbox entry.</summary>
/// <param name="Type"><c>mention</c>, <c>reply</c> or <c>like</c>.</param>
/// <param name="Others">How many more liked the beep before <paramref name="Sender"/>.</param>
/// <param name="Earlier">The newest of those, newest first.</param>
public sealed record NotificationProps(
    Guid Id,
    string Type,
    [property: JsonPropertyName("from")] ActorProps Sender,
    DateTimeOffset CreatedAt,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] bool Read,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] int Others,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<ActorProps>? Earlier,
    BeepProps Beep)
{
    public static NotificationProps From(NotificationView notification)
    {
        ArgumentNullException.ThrowIfNull(notification);
        return new NotificationProps(
            notification.Id,
            notification.Type.ToString().ToLowerInvariant(),
            ActorProps.From(notification.From),
            notification.CreatedAt,
            notification.Read,
            notification.Others,
            notification.Earlier is { Count: > 0 } earlier ? [.. earlier.Select(ActorProps.From)] : null,
            BeepProps.From(notification.Beep));
    }
}

/// <summary>The posting form (components/Composer.tsx). The text field's errors come with the island's.</summary>
/// <param name="ReturnUrl">Where to go once the beep is posted.</param>
public sealed record ComposerProps(
    string ReturnUrl,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] Guid? ReplyTo,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Text);

/// <summary>What <c>/live/beeps</c> and <c>/live/notifications</c> answer.</summary>
public sealed record LiveItems<T>(IReadOnlyList<T> Items);

/// <summary>A page of something with the address of the next one, for lists that load more as they are scrolled.</summary>
public sealed record PageProps<T>(IReadOnlyList<T> Items, [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Next);

public static class PageProps
{
    public static PageProps<T> From<TSource, T>(CursorPage<TSource> page, Func<TSource, T> map, Func<Guid, string> next)
    {
        ArgumentNullException.ThrowIfNull(page);
        ArgumentNullException.ThrowIfNull(map);
        ArgumentNullException.ThrowIfNull(next);
        return new PageProps<T>([.. page.Items.Select(map)], page.NextCursor is { } cursor ? next(cursor) : null);
    }
}
