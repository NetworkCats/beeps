using System.Security.Cryptography;
using System.Text;
using Beeps.Features.Shared;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Options;

namespace Beeps.Features.RateLimiting;

public sealed partial class RateLimitService(
    IRateLimitStore store,
    IOptionsMonitor<RateLimitOptions> options,
    IStringLocalizer<SharedResource> localizer,
    ILogger<RateLimitService> logger)
{
    public Task<Result> CheckAsync(string policy, string partition, CancellationToken cancellationToken) =>
        CheckAsync([new RateLimitTarget(policy, partition)], cancellationToken);

    /// <summary>
    /// Checks several policies at once (e.g. login by IP and by target account, or an agent's own quota
    /// plus its owner's total). Either all of them count the request, or none does.
    /// </summary>
    public async Task<Result> CheckAsync(IReadOnlyList<RateLimitTarget> targets, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(targets);
        var current = options.CurrentValue;
        if (!current.Enabled || targets.Count == 0)
        {
            return Result.Success;
        }

        var counters = new List<RateLimitCounter>();
        foreach (var target in targets)
        {
            if (!current.Policies.TryGetValue(target.Policy, out var policy))
            {
                throw new InvalidOperationException($"Rate limit policy '{target.Policy}' is not configured.");
            }

            var partition = HashPartition(target.Partition);
            foreach (var rule in policy.Rules)
            {
                var windowSeconds = (long)rule.Window.TotalSeconds;
                counters.Add(new RateLimitCounter($"{target.Policy}:{windowSeconds}:{partition}", rule.PermitLimit, rule.Window));
            }
        }

        RateLimitDecision decision;
        try
        {
            decision = await store.TryAcquireAsync(counters, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // Fail open: a cache outage must not take the whole site down. Logged as an error so it
            // is noticed; Turnstile and Identity lockout still protect the sensitive flows meanwhile.
            LogStoreFailed(logger, exception);
            return Result.Success;
        }

        return decision.IsAllowed
            ? Result.Success
            : Error.RateLimited(decision.RetryAfter, localizer["RateLimit.Detail"]);
    }

    // Keys never contain raw IPs or email addresses.
    private static string HashPartition(string partition) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(partition)).AsSpan(0, 16));

    [LoggerMessage(Level = LogLevel.Error, Message = "Rate limit store failed; allowing the request")]
    private static partial void LogStoreFailed(ILogger logger, Exception exception);
}
