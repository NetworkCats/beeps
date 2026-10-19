using Beeps.Features.Accounts;
using Beeps.Features.Beeps;

namespace Beeps.Features.Search;

/// <summary>A beep as the search index holds it (PLAN 6.18). Only publicly visible beeps are indexed.</summary>
/// <param name="Hashtags">Normalized.</param>
public sealed record BeepDocument(Guid Id, string Text, Guid AuthorId, IReadOnlyList<string> Hashtags, DateTimeOffset CreatedAt);

/// <summary>A human or a connected agent as the search index holds it.</summary>
public sealed record ActorDocument(Guid Id, ActorKind Kind, string Handle, string? DisplayName, string? Bio);

/// <summary>What the index should hold for an actor: its document, or nothing (deleted, banned, not connected yet).</summary>
public sealed record ActorDocumentState(Guid Id, ActorDocument? Document);

/// <summary>Ids in ranking order. <see cref="EstimatedTotal"/> is the index's estimate of all matches.</summary>
public sealed record SearchHits(IReadOnlyList<Guid> Ids, int EstimatedTotal);

/// <summary>A visible actor with its bio, for showing search results.</summary>
public sealed record ActorSearchRow(ActorRow Actor, string? Bio);

/// <param name="NextOffset">Pass back as <c>offset</c> for more results; null at the end.</param>
public sealed record SearchPage<T>(IReadOnlyList<T> Items, int? NextOffset);

/// <param name="Type"><see cref="SearchTypes.Beeps"/> (the default) or <see cref="SearchTypes.Users"/>.</param>
/// <param name="Hashtag">For beeps only.</param>
public sealed record SearchRequest(string? Query, string? Type = null, string? Hashtag = null, int? Offset = null, int? Limit = null);

/// <summary>One page of results: beeps or people, depending on what was searched.</summary>
public sealed record SearchResults(IReadOnlyList<BeepView> Beeps, IReadOnlyList<ActorSearchResult> Users, int? NextOffset);

public sealed record ActorSearchResult(ActorView Actor, string? Bio);

/// <summary>The search engine (Meilisearch). Written only by the sync jobs and the rebuild (PLAN 12); searches return ids only.</summary>
public interface ISearchIndex
{
    Task UpsertBeepsAsync(IReadOnlyCollection<BeepDocument> beeps, CancellationToken cancellationToken);

    Task DeleteBeepsAsync(IReadOnlyCollection<Guid> beepIds, CancellationToken cancellationToken);

    Task DeleteBeepsByAuthorsAsync(IReadOnlyCollection<Guid> authorIds, CancellationToken cancellationToken);

    Task UpsertActorsAsync(IReadOnlyCollection<ActorDocument> actors, CancellationToken cancellationToken);

    Task DeleteActorsAsync(IReadOnlyCollection<Guid> actorIds, CancellationToken cancellationToken);

    /// <param name="query">Empty to list everything with <paramref name="hashtag"/>, newest first.</param>
    /// <param name="hashtag">Normalized; null for no filter.</param>
    Task<SearchHits> SearchBeepsAsync(string query, string? hashtag, int offset, int limit, CancellationToken cancellationToken);

    Task<SearchHits> SearchActorsAsync(string query, int offset, int limit, CancellationToken cancellationToken);

    /// <summary>Starts filling fresh indexes from scratch; searches keep using the current ones until it completes.</summary>
    Task<ISearchRebuild> StartRebuildAsync(CancellationToken cancellationToken);

    /// <summary>Creates a key that can only search and write documents, for the app (PLAN 6.18); returns the key.</summary>
    Task<string> CreateAppKeyAsync(CancellationToken cancellationToken);
}

public interface ISearchRebuild : IAsyncDisposable
{
    Task AddBeepsAsync(IReadOnlyCollection<BeepDocument> beeps, CancellationToken cancellationToken);

    Task AddActorsAsync(IReadOnlyCollection<ActorDocument> actors, CancellationToken cancellationToken);

    /// <summary>Puts the fresh indexes in place of the current ones. Disposing without completing throws the fresh ones away.</summary>
    Task CompleteAsync(CancellationToken cancellationToken);
}

/// <summary>The database side of search: what the index should contain, and what the results show (PLAN 6.18: PostgreSQL decides).</summary>
public interface ISearchStore
{
    /// <summary>The beep's document if anyone may see it (not deleted, not hidden, author visible); otherwise null.</summary>
    Task<BeepDocument?> FindBeepAsync(Guid beepId, CancellationToken cancellationToken);

    /// <summary>The actor and, for a human, all of their agents, deleted ones included.</summary>
    Task<IReadOnlyList<ActorDocumentState>> GetAccountAsync(Guid actorId, CancellationToken cancellationToken);

    /// <summary>Publicly visible beeps after <paramref name="after"/> in id order, by these authors or by anyone.</summary>
    Task<IReadOnlyList<BeepDocument>> ListBeepsAsync(IReadOnlyCollection<Guid>? authorIds, Guid? after, int limit, CancellationToken cancellationToken);

    /// <summary>Visible actors that have a handle, after <paramref name="after"/> in id order.</summary>
    Task<IReadOnlyList<ActorDocument>> ListActorsAsync(Guid? after, int limit, CancellationToken cancellationToken);

    /// <summary>The visible ones among these actors, none the viewer blocked, in no particular order.</summary>
    Task<IReadOnlyList<ActorSearchRow>> GetActorsAsync(IReadOnlyCollection<Guid> actorIds, Guid? viewerId, CancellationToken cancellationToken);

    /// <summary>
    /// Findable actors whose handle starts with <paramref name="normalizedPrefix"/>, for completing an @mention: the
    /// viewer's own agents first, then those they follow, then shorter handles (of one length, in byte order). Never the
    /// viewer, nor anyone they blocked.
    /// </summary>
    Task<IReadOnlyList<ActorRow>> SuggestActorsAsync(string normalizedPrefix, Viewer viewer, int limit, CancellationToken cancellationToken);
}

public static class SearchErrorCodes
{
    public const string QueryInvalid = "search_query_invalid";
    public const string TypeInvalid = "search_type_invalid";
    public const string Unavailable = "search_unavailable";
}

/// <summary>Values of <c>type</c>; published.</summary>
public static class SearchTypes
{
    public const string Beeps = "beeps";
    public const string Users = "users";
}
