using System.ComponentModel.DataAnnotations;

namespace Beeps.Infrastructure.Search;

public sealed class SearchOptions
{
    public const string SectionName = "Search";

    /// <summary>Meilisearch on the internal network only; never exposed to browsers or agents.</summary>
    [Required]
    public string Url { get; set; } = "";

    /// <summary>The app's key: search and document writes on its own indexes, nothing else (create it with <c>search create-key</c>).</summary>
    [Required]
    public string ApiKey { get; set; } = "";

    /// <summary>
    /// Only for the <c>search rebuild</c> and <c>search create-key</c> commands, which create indexes, change
    /// settings and create keys. Not set on the running app. Falls back to <see cref="ApiKey"/>.
    /// </summary>
    public string? AdminKey { get; set; }

    /// <summary>A search request that takes longer fails, so a slow engine cannot hold up page requests.</summary>
    public TimeSpan SearchTimeout { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>How long a write waits for Meilisearch to apply it before the sync job counts it as failed and retries.</summary>
    public TimeSpan TaskTimeout { get; set; } = TimeSpan.FromSeconds(30);
}
