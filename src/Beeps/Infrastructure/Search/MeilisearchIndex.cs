using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Beeps.Features.Accounts;
using Beeps.Features.Search;
using Meilisearch;
using Microsoft.Extensions.Options;

namespace Beeps.Infrastructure.Search;

/// <summary>
/// The two Meilisearch indexes of PLAN 6.18, <c>beeps</c> and <c>actors</c>. Documents are written as JSON made by
/// source generation; searches ask for ids only. Every write waits for Meilisearch to apply it, so a failed write
/// fails its sync job, which is then retried.
/// </summary>
public sealed class MeilisearchIndex : ISearchIndex
{
    public const string BeepsIndex = "beeps";
    public const string ActorsIndex = "actors";
    public const string RebuildSuffix = "_rebuild";

    private const string PrimaryKey = "id";
    private const int TaskPollMilliseconds = 50;

    private readonly MeilisearchClient client;
    private readonly Lazy<MeilisearchClient> adminClient;
    private readonly SearchOptions options;

    public MeilisearchIndex(IOptions<SearchOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        this.options = options.Value;
        client = new MeilisearchClient(this.options.Url, this.options.ApiKey);
        adminClient = new Lazy<MeilisearchClient>(() => new MeilisearchClient(this.options.Url, this.options.AdminKey ?? this.options.ApiKey));
    }

    public Task UpsertBeepsAsync(IReadOnlyCollection<BeepDocument> beeps, CancellationToken cancellationToken) =>
        AddAsync(client, BeepsIndex, BeepsJson(beeps), cancellationToken);

    public Task DeleteBeepsAsync(IReadOnlyCollection<Guid> beepIds, CancellationToken cancellationToken) =>
        RunAsync(client, client.Index(BeepsIndex).DeleteDocumentsAsync(Ids(beepIds), cancellationToken), cancellationToken);

    public Task DeleteBeepsByAuthorsAsync(IReadOnlyCollection<Guid> authorIds, CancellationToken cancellationToken) =>
        RunAsync(
            client,
            client.Index(BeepsIndex).DeleteDocumentsAsync(new Meilisearch.QueryParameters.DeleteDocumentsQuery { Filter = $"author_id IN [{string.Join(", ", Ids(authorIds).Select(Quote))}]" }, cancellationToken),
            cancellationToken);

    public Task UpsertActorsAsync(IReadOnlyCollection<ActorDocument> actors, CancellationToken cancellationToken) =>
        AddAsync(client, ActorsIndex, ActorsJson(actors), cancellationToken);

    public Task DeleteActorsAsync(IReadOnlyCollection<Guid> actorIds, CancellationToken cancellationToken) =>
        RunAsync(client, client.Index(ActorsIndex).DeleteDocumentsAsync(Ids(actorIds), cancellationToken), cancellationToken);

    public Task<SearchHits> SearchBeepsAsync(string query, string? hashtag, int offset, int limit, CancellationToken cancellationToken)
    {
        var search = new SearchQuery { Offset = offset, Limit = limit, AttributesToRetrieve = [PrimaryKey] };
        if (hashtag is not null)
        {
            search.Filter = $"hashtags = {Quote(hashtag)}";
        }

        // Without words there is nothing to rank by; a hashtag's beeps then come newest first, like its page.
        if (query.Length == 0)
        {
            search.Sort = ["created_at:desc"];
        }

        return SearchAsync(BeepsIndex, query, search, cancellationToken);
    }

    public Task<SearchHits> SearchActorsAsync(string query, int offset, int limit, CancellationToken cancellationToken) =>
        SearchAsync(ActorsIndex, query, new SearchQuery { Offset = offset, Limit = limit, AttributesToRetrieve = [PrimaryKey] }, cancellationToken);

    public async Task<ISearchRebuild> StartRebuildAsync(CancellationToken cancellationToken)
    {
        var admin = adminClient.Value;
        foreach (var name in new[] { BeepsIndex, ActorsIndex })
        {
            // Left over from a rebuild that did not finish.
            await RunAsync(admin, admin.DeleteIndexAsync(name + RebuildSuffix, cancellationToken), cancellationToken, allowMissing: true);
            await RunAsync(admin, admin.CreateIndexAsync(name + RebuildSuffix, PrimaryKey, cancellationToken), cancellationToken);
            await RunAsync(admin, admin.Index(name + RebuildSuffix).UpdateSettingsAsync(SettingsFor(name), cancellationToken), cancellationToken);
        }

        return new Rebuild(this, admin);
    }

    public async Task<string> CreateAppKeyAsync(CancellationToken cancellationToken)
    {
        var key = await adminClient.Value.CreateKeyAsync(
            new Key
            {
                Name = "beeps-app",
                Description = "The beeps app: searches and keeps documents in sync. Created by `search create-key`.",
                Actions = [KeyAction.Search, KeyAction.DocumentsAdd, KeyAction.DocumentsDelete, KeyAction.TasksGet],
                Indexes = [BeepsIndex, ActorsIndex],
                ExpiresAt = null,
            },
            cancellationToken);
        return key.KeyUid;
    }

    /// <summary>What each index can be searched, filtered and sorted by.</summary>
    private static Settings SettingsFor(string index) => index == BeepsIndex
        ? new Settings
        {
            SearchableAttributes = ["text", "hashtags"],
            FilterableAttributes = ["hashtags", "author_id"],
            SortableAttributes = ["created_at"],
            // Meilisearch's own order, then newer before older among equally good matches.
            RankingRules = ["words", "typo", "proximity", "attribute", "sort", "exactness", "created_at:desc"],
            Pagination = new Pagination { MaxTotalHits = SearchService.MaxResults },
        }
        : new Settings
        {
            SearchableAttributes = ["handle", "display_name", "bio"],
            FilterableAttributes = ["kind"],
            Pagination = new Pagination { MaxTotalHits = SearchService.MaxResults },
        };

    private async Task<SearchHits> SearchAsync(string index, string query, SearchQuery search, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(options.SearchTimeout);
        ISearchable<IdHit> result;
        try
        {
            result = await client.Index(index).SearchAsync<IdHit>(query, search, timeout.Token);
        }
        catch (MeilisearchApiError error) when (error.Code == "index_not_found")
        {
            // Nothing indexed yet: run `search rebuild` once (README, launch checklist).
            return new SearchHits([], 0);
        }

        var ids = result.Hits.Select(h => Guid.TryParse(h.Id, CultureInfo.InvariantCulture, out var id) ? id : Guid.Empty).Where(id => id != Guid.Empty).ToList();
        return new SearchHits(ids, result is SearchResult<IdHit> page ? page.EstimatedTotalHits : ids.Count);
    }

    private Task AddAsync(MeilisearchClient target, string index, string json, CancellationToken cancellationToken) =>
        RunAsync(target, target.Index(index).AddDocumentsJsonAsync(json, PrimaryKey, cancellationToken), cancellationToken);

    /// <summary>Waits for the task Meilisearch queued and throws if it did not succeed.</summary>
    private async Task RunAsync(MeilisearchClient target, Task<TaskInfo> start, CancellationToken cancellationToken, bool allowMissing = false)
    {
        var queued = await start;
        var done = await target.WaitForTaskAsync(queued.TaskUid, options.TaskTimeout.TotalMilliseconds, TaskPollMilliseconds, cancellationToken);
        if (done.Status == TaskInfoStatus.Succeeded
            || (allowMissing && done.Error?.GetValueOrDefault("code") == "index_not_found"))
        {
            return;
        }

        var reason = done.Error is { } error ? $"{error.GetValueOrDefault("code")}: {error.GetValueOrDefault("message")}" : done.Status.ToString();
        throw new InvalidOperationException($"Meilisearch task {done.Uid} ({done.Type}) did not succeed: {reason}");
    }

    private static string BeepsJson(IReadOnlyCollection<BeepDocument> beeps) =>
        JsonSerializer.Serialize(
            beeps.Select(b => new BeepJson(b.Id.ToString("D"), b.Text, b.AuthorId.ToString("D"), b.Hashtags, b.CreatedAt.ToUnixTimeSeconds())).ToList(),
            SearchDocumentJsonContext.Default.ListBeepJson);

    private static string ActorsJson(IReadOnlyCollection<ActorDocument> actors) =>
        JsonSerializer.Serialize(
            actors.Select(a => new ActorJson(a.Id.ToString("D"), a.Handle, a.DisplayName, a.Bio, a.Kind == ActorKind.Agent ? "agent" : "human")).ToList(),
            SearchDocumentJsonContext.Default.ListActorJson);

    private static IEnumerable<string> Ids(IEnumerable<Guid> ids) => ids.Select(id => id.ToString("D"));

    /// <summary>A string literal in Meilisearch's filter syntax.</summary>
    private static string Quote(string value)
    {
        var quoted = new StringBuilder(value.Length + 2).Append('"');
        foreach (var c in value)
        {
            if (c is '"' or '\\')
            {
                quoted.Append('\\');
            }

            quoted.Append(c);
        }

        return quoted.Append('"').ToString();
    }

    private sealed class Rebuild(MeilisearchIndex owner, MeilisearchClient admin) : ISearchRebuild
    {
        public Task AddBeepsAsync(IReadOnlyCollection<BeepDocument> beeps, CancellationToken cancellationToken) =>
            owner.AddAsync(admin, BeepsIndex + RebuildSuffix, BeepsJson(beeps), cancellationToken);

        public Task AddActorsAsync(IReadOnlyCollection<ActorDocument> actors, CancellationToken cancellationToken) =>
            owner.AddAsync(admin, ActorsIndex + RebuildSuffix, ActorsJson(actors), cancellationToken);

        public async Task CompleteAsync(CancellationToken cancellationToken)
        {
            var swaps = new List<IndexSwap>();
            foreach (var name in new[] { BeepsIndex, ActorsIndex })
            {
                // The very first time there is nothing to swap with: the fresh index is renamed (first to second).
                swaps.Add(new IndexSwap(name + RebuildSuffix, name, rename: !await ExistsAsync(name, cancellationToken)));
            }

            await owner.RunAsync(admin, admin.SwapIndexesAsync(swaps, cancellationToken), cancellationToken);
        }

        public async ValueTask DisposeAsync()
        {
            // After a swap these hold the old documents; without one, an unfinished copy. Either way they go.
            foreach (var name in new[] { BeepsIndex, ActorsIndex })
            {
                await owner.RunAsync(admin, admin.DeleteIndexAsync(name + RebuildSuffix, CancellationToken.None), CancellationToken.None, allowMissing: true);
            }
        }

        private async Task<bool> ExistsAsync(string name, CancellationToken cancellationToken)
        {
            try
            {
                await admin.GetIndexAsync(name, cancellationToken);
                return true;
            }
            catch (MeilisearchApiError error) when (error.Code == "index_not_found")
            {
                return false;
            }
        }
    }

    private sealed class IdHit
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = "";
    }
}

/// <param name="CreatedAt">Unix seconds, so it can be sorted on.</param>
internal sealed record BeepJson(string Id, string Text, string AuthorId, IReadOnlyList<string> Hashtags, long CreatedAt);

internal sealed record ActorJson(string Id, string Handle, string? DisplayName, string? Bio, string Kind);

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower)]
[JsonSerializable(typeof(List<BeepJson>))]
[JsonSerializable(typeof(List<ActorJson>))]
internal sealed partial class SearchDocumentJsonContext : JsonSerializerContext;
