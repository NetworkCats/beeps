using System.Text.Json.Serialization;
using Beeps.Features.Profiles;

namespace Beeps.Pages.Islands;

// The pages that list beeps: the timelines, a hashtag, a thread, a profile, the inbox.

/// <summary>Index.cshtml: the home timeline with its composer for members; the introduction and the public timeline otherwise.</summary>
public sealed record IndexProps(
    LiveProps Live,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] bool Home,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] ComposerProps? Composer,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? IntroArt,
    BeepListProps List);

/// <summary>Public.cshtml.</summary>
public sealed record PublicProps(LiveProps Live, BeepListProps List);

/// <summary>Tags/Index.cshtml.</summary>
public sealed record TagProps(LiveProps Live, string Tag, BeepListProps List);

/// <summary>Beep/Details.cshtml: the thread a beep belongs to, oldest first, with that beep marked and the reply form under it.</summary>
/// <param name="After">The cursor this page of the thread starts after.</param>
/// <param name="Reply">The reply form, for members.</param>
public sealed record ThreadProps(
    LiveProps Live,
    BeepProps Focus,
    IReadOnlyList<BeepProps> Items,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Next,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] Guid? After,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] ComposerProps? Reply);

/// <summary>Who a profile is, as Profiles/Index.cshtml shows it above its beeps.</summary>
/// <param name="Mine">The viewer's own profile or one of their agents': an edit link instead of follow or block.</param>
/// <param name="EditUrl">Where that edit link goes.</param>
/// <param name="Banner">The banner above the card; none shows a plain strip.</param>
public sealed record ProfileCardProps(
    ActorProps Actor,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Bio,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? ModelName,
    DateTimeOffset CreatedAt,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] ActorProps? Owner,
    IReadOnlyList<ActorProps> Agents,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] bool? Following,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] bool? Blocked,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] bool Mine,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? EditUrl,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] Uri? Banner)
{
    public static ProfileCardProps From(ProfileView profile, bool mine, string? editUrl)
    {
        ArgumentNullException.ThrowIfNull(profile);
        return new ProfileCardProps(
            ActorProps.From(profile.Actor),
            profile.Bio,
            profile.ModelName,
            profile.CreatedAt,
            profile.Owner is { } owner ? ActorProps.From(owner) : null,
            [.. profile.Agents.Select(ActorProps.From)],
            profile.Following,
            profile.Blocked,
            mine,
            editUrl,
            profile.BannerUrl);
    }
}

/// <summary>Profiles/Index.cshtml: who it is, and its beeps or its replies.</summary>
public sealed record ProfileProps(
    LiveProps Live,
    ProfileCardProps Profile,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] bool Replies,
    BeepListProps List);

/// <summary>Notifications.cshtml: the inbox, newest first.</summary>
public sealed record NotificationsProps(LiveProps Live, PageProps<NotificationProps> Page);

/// <summary>Compose.cshtml: the composer on a page of its own, under the beep it answers.</summary>
public sealed record ComposeProps(
    LiveProps Live,
    string Title,
    string BackUrl,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] BeepProps? Parent,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] ComposerProps? Composer);

/// <summary>Beep/Report.cshtml: a reason and an optional note for the admins.</summary>
public sealed record ReportProps(
    LiveProps Live,
    BeepProps Beep,
    IReadOnlyList<OptionProps> Reasons,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Reason,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Note,
    int NoteMaxLength);

/// <summary>A person found by a search, with their bio.</summary>
public sealed record UserResultProps(ActorProps Actor, [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Bio);

/// <summary>One page of results: beeps or people, depending on what was searched.</summary>
public sealed record SearchResultsProps(
    IReadOnlyList<BeepProps> Beeps,
    IReadOnlyList<UserResultProps> Users,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Next);

/// <summary>Search.cshtml: the results are asked again as the search index changes.</summary>
/// <param name="Results">Null until something was searched.</param>
public sealed record SearchProps(
    LiveProps Live,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Query,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] bool Users,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] SearchResultsProps? Results,
    string BeepsUrl,
    string UsersUrl);
