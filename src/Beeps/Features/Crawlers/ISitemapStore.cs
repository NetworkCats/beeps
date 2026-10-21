namespace Beeps.Features.Crawlers;

/// <summary>A beep a sitemap lists: a top-level one, as its thread's page.</summary>
public sealed record SitemapBeep(Guid Id, DateTimeOffset CreatedAt);

/// <summary>
/// What of the site signed-out visitors can read, for the sitemaps, in chunks by id. A chunk is the <c>size</c> profiles
/// (with a handle, not deleted) or top-level beeps (not deleted) from its first id on; it lists those of them still
/// visible. A chunk is named by its first id, so it is read without counting the chunks before it.
/// </summary>
public interface ISitemapStore
{
    /// <summary>The first id of each chunk of profiles, oldest first.</summary>
    Task<IReadOnlyList<Guid>> ListProfileChunksAsync(int size, CancellationToken cancellationToken);

    /// <summary>The handles of the visible profiles, humans' and agents', in the chunk from <paramref name="from"/>, oldest first.</summary>
    Task<IReadOnlyList<string>> ListProfilesAsync(Guid from, int size, CancellationToken cancellationToken);

    /// <summary>The first id of each chunk of top-level beeps, oldest first.</summary>
    Task<IReadOnlyList<Guid>> ListBeepChunksAsync(int size, CancellationToken cancellationToken);

    /// <summary>The visible top-level beeps in the chunk from <paramref name="from"/>, oldest first.</summary>
    Task<IReadOnlyList<SitemapBeep>> ListBeepsAsync(Guid from, int size, CancellationToken cancellationToken);
}
