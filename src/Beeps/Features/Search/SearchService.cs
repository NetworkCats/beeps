using System.Text.RegularExpressions;
using Beeps.Features.Accounts;
using Beeps.Features.Beeps;
using Beeps.Features.Shared;
using Microsoft.Extensions.Localization;

namespace Beeps.Features.Search;

/// <summary>
/// Searching beeps and people. The index only supplies ids in ranking order; what is shown, and to
/// whom, comes from PostgreSQL, so a document the index has not caught up on yet never leaks anything.
/// </summary>
public sealed partial class SearchService(
    ISearchIndex index,
    ISearchStore store,
    IBeepStore beeps,
    BeepViews views,
    IStringLocalizer<SharedResource> localizer,
    ILogger<SearchService> logger)
{
    /// <summary>In user-perceived characters, like beeps.</summary>
    public const int MaxQueryLength = 100;

    /// <summary>Results beyond this are not reachable by paging.</summary>
    public const int MaxResults = 1000;

    public const int DefaultLimit = 20;

    public const int MaxLimit = 50;

    /// <summary>People offered for an @mention being typed: a few, so the list stays small.</summary>
    public const int MaxSuggestions = 3;

    private static readonly LogThrottle SearchFailures = new();

    /// <summary>
    /// What the search box does with <c>#tag</c>: the hashtag's page, not a text search. Returns the
    /// normalized tag, or null for any other query.
    /// </summary>
    public static string? HashtagShortcut(string? query)
    {
        var trimmed = query?.Trim();
        return trimmed is ['#', ..] && BeepText.TryNormalizeHashtag(trimmed, out var tag) ? tag : null;
    }

    /// <summary>Beeps or people, by <see cref="SearchRequest.Type"/>.</summary>
    public async Task<Result<SearchResults>> SearchAsync(Viewer? viewer, SearchRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        switch (string.IsNullOrEmpty(request.Type) ? SearchTypes.Beeps : request.Type)
        {
            case SearchTypes.Beeps:
                var beeps = await SearchBeepsAsync(viewer, request.Query, request.Hashtag, request.Offset, request.Limit, cancellationToken);
                return beeps.IsSuccess ? new SearchResults(beeps.Value.Items, [], beeps.Value.NextOffset) : beeps.Error;
            case SearchTypes.Users:
                var users = await SearchUsersAsync(viewer, request.Query, request.Offset, request.Limit, cancellationToken);
                return users.IsSuccess ? new SearchResults([], users.Value.Items, users.Value.NextOffset) : users.Error;
            default:
                return Error.Validation(SearchErrorCodes.TypeInvalid, localizer["Error.SearchTypeInvalid"]);
        }
    }

    /// <param name="hashtag">Only beeps with this hashtag (with or without <c>#</c>); the query may then be empty.</param>
    public async Task<Result<SearchPage<BeepView>>> SearchBeepsAsync(Viewer? viewer, string? query, string? hashtag, int? offset, int? limit, CancellationToken cancellationToken)
    {
        string? tag = null;
        if (!string.IsNullOrWhiteSpace(hashtag) && !BeepText.TryNormalizeHashtag(hashtag, out tag))
        {
            return Error.Validation(BeepErrorCodes.InvalidHashtag, localizer["Error.InvalidHashtag"]);
        }

        var request = Check(query, offset, limit, allowEmpty: tag is not null);
        if (!request.IsSuccess)
        {
            return request.Error;
        }

        var (text, from, count) = request.Value;
        var hits = await SearchAsync(() => index.SearchBeepsAsync(text, tag, from, count, cancellationToken));
        if (!hits.IsSuccess)
        {
            return hits.Error;
        }

        var rows = (await beeps.GetRowsAsync(hits.Value.Ids, viewer?.ActorId, cancellationToken)).ToDictionary(r => r.Id);
        var ranked = hits.Value.Ids.Where(rows.ContainsKey).Select(id => rows[id]).ToList();
        return new SearchPage<BeepView>(await views.BuildAsync(ranked, viewer, cancellationToken), NextOffset(from, count, hits.Value));
    }

    public async Task<Result<SearchPage<ActorSearchResult>>> SearchUsersAsync(Viewer? viewer, string? query, int? offset, int? limit, CancellationToken cancellationToken)
    {
        var request = Check(query, offset, limit, allowEmpty: false);
        if (!request.IsSuccess)
        {
            return request.Error;
        }

        var (text, from, count) = request.Value;
        var hits = await SearchAsync(() => index.SearchActorsAsync(text, from, count, cancellationToken));
        if (!hits.IsSuccess)
        {
            return hits.Error;
        }

        var rows = (await store.GetActorsAsync(hits.Value.Ids, viewer?.ActorId, cancellationToken)).ToDictionary(r => r.Actor.Id);
        return new SearchPage<ActorSearchResult>(
            [.. hits.Value.Ids.Where(rows.ContainsKey).Select(id => new ActorSearchResult(views.Actor(rows[id].Actor, viewer), rows[id].Bio))],
            NextOffset(from, count, hits.Value));
    }

    /// <summary>
    /// Whom an @mention being typed may mean, by the start of their handle (<paramref name="prefix"/>, without the
    /// <c>@</c>). Asked of PostgreSQL, not the index: it answers as each letter is typed, and a prefix needs no ranking.
    /// Nobody for text that no handle starts with.
    /// </summary>
    public async Task<IReadOnlyList<ActorView>> SuggestMentionsAsync(Viewer viewer, string? prefix, CancellationToken cancellationToken)
    {
        if (prefix is not { Length: > 0 and <= Actor.HandleMaxLength } || !HandlePrefixPattern().IsMatch(prefix))
        {
            return [];
        }

        var rows = await store.SuggestActorsAsync(AccountRules.NormalizeHandle(prefix), viewer, MaxSuggestions, cancellationToken);
        return [.. rows.Select(row => views.Actor(row, viewer))];
    }

    private Result<(string Text, int Offset, int Limit)> Check(string? query, int? offset, int? limit, bool allowEmpty)
    {
        var text = (query ?? "").Trim();
        if ((text.Length == 0 && !allowEmpty)
            || BeepText.Length(text) > MaxQueryLength
            || TextRules.HasForbiddenCharacters(text, allowLineBreaks: false))
        {
            return Error.Validation(SearchErrorCodes.QueryInvalid, localizer["Error.SearchQueryInvalid", MaxQueryLength]);
        }

        var from = Math.Clamp(offset ?? 0, 0, MaxResults);
        var count = Math.Min(Math.Clamp(limit ?? DefaultLimit, 1, MaxLimit), MaxResults - from);
        return count == 0
            ? Error.Validation(SearchErrorCodes.QueryInvalid, localizer["Error.SearchTooFar", MaxResults])
            : (text, from, count);
    }

    private static int? NextOffset(int offset, int limit, SearchHits hits)
    {
        var next = offset + limit;
        return hits.Ids.Count == limit && next < Math.Min(hits.EstimatedTotal, MaxResults) ? next : null;
    }

    // The engine is a separate service; when it is away, searching fails but nothing else does.
    private async Task<Result<SearchHits>> SearchAsync(Func<Task<SearchHits>> search)
    {
        try
        {
            return await search();
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            if (SearchFailures.Allow())
            {
                LogSearchFailed(logger, exception);
            }
            return Error.Unavailable(SearchErrorCodes.Unavailable, localizer["Error.SearchUnavailable"]);
        }
    }

    /// <summary>A username, or an agent's <c>owner.name</c> with its name begun or not yet (as <see cref="BeepText"/> reads mentions).</summary>
    [GeneratedRegex(@"^[A-Za-z0-9_]+(\.[A-Za-z0-9_]*)?\z")]
    private static partial Regex HandlePrefixPattern();

    [LoggerMessage(Level = LogLevel.Error, Message = "Search failed (logged at most once a minute)")]
    private static partial void LogSearchFailed(ILogger logger, Exception exception);
}
