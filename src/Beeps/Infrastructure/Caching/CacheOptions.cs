using Microsoft.Extensions.Options;

namespace Beeps.Infrastructure.Caching;

/// <summary>
/// The content cache: database reads kept in Valkey, with a short-lived copy in each
/// instance. Every entry is dropped when what it was read from changes, so the lifetimes only bound how long
/// an entry lives when nothing changes, and how long a missed invalidation can last.
/// </summary>
public sealed class CacheOptions
{
    public const string SectionName = "Cache";

    /// <summary>Off: every read goes to PostgreSQL (for measuring, or ruling the cache out).</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// How long an instance keeps its own copy. Kept short (instances are stateless): changes reach other instances
    /// through Pub/Sub, and this bounds the damage when a message is lost.
    /// </summary>
    public TimeSpan LocalExpiration { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>Memory an instance gives its own copies, in megabytes (counted as their JSON size).</summary>
    public int LocalSizeLimitMegabytes { get; set; } = 64;

    /// <summary>Beeps: text, author, thread, mentions and images. Counts come live from their own hashes.</summary>
    public TimeSpan BeepExpiration { get; set; } = TimeSpan.FromHours(1);

    /// <summary>Actors: names, handle, avatar, bio, and whether they are deleted or banned.</summary>
    public TimeSpan ActorExpiration { get; set; } = TimeSpan.FromHours(1);

    /// <summary>Handle → actor, who a human follows, which agents a human has.</summary>
    public TimeSpan ListExpiration { get; set; } = TimeSpan.FromHours(1);

    /// <summary>Hashtag name → id. Hashtags are never renamed or deleted, so nothing ever invalidates these.</summary>
    public TimeSpan HashtagExpiration { get; set; } = TimeSpan.FromDays(1);

    /// <summary>
    /// Invalidations run once right after the change and once more this much later. The second pass drops what
    /// a read that started before the change wrote back after the first pass.
    /// </summary>
    public TimeSpan SecondPassDelay { get; set; } = TimeSpan.FromSeconds(2);

    /// <summary>After a Valkey error, reads skip Valkey for this long and go to PostgreSQL directly.</summary>
    public TimeSpan SharedPauseAfterError { get; set; } = TimeSpan.FromSeconds(5);
}

internal sealed class CacheOptionsValidator : IValidateOptions<CacheOptions>
{
    public ValidateOptionsResult Validate(string? name, CacheOptions options)
    {
        List<string> failures = [];
        if (options.LocalExpiration <= TimeSpan.Zero || options.LocalExpiration > TimeSpan.FromMinutes(5))
        {
            failures.Add("Cache:LocalExpiration must be between zero and five minutes.");
        }

        if (options.LocalSizeLimitMegabytes < 1)
        {
            failures.Add("Cache:LocalSizeLimitMegabytes must be at least 1.");
        }

        foreach (var (key, value) in new[]
                 {
                     ("BeepExpiration", options.BeepExpiration),
                     ("ActorExpiration", options.ActorExpiration),
                     ("ListExpiration", options.ListExpiration),
                     ("HashtagExpiration", options.HashtagExpiration),
                 })
        {
            if (value < options.LocalExpiration)
            {
                failures.Add($"Cache:{key} must not be shorter than Cache:LocalExpiration.");
            }
        }

        // The cached count columns only show for beeps without live counts, which by then were written back
        // and expired (Redis.RedisBeepCounterStore.CountsLifetime); a cached copy must not outlive that.
        if (options.BeepExpiration >= Redis.RedisBeepCounterStore.CountsLifetime)
        {
            failures.Add($"Cache:BeepExpiration must be shorter than {Redis.RedisBeepCounterStore.CountsLifetime}.");
        }

        if (options.SecondPassDelay <= TimeSpan.Zero)
        {
            failures.Add("Cache:SecondPassDelay must be positive.");
        }

        if (options.SharedPauseAfterError < TimeSpan.Zero)
        {
            failures.Add("Cache:SharedPauseAfterError must not be negative.");
        }

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }
}
