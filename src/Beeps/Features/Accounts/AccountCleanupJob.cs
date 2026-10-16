using Beeps.Features.Shared;

namespace Beeps.Features.Accounts;

/// <summary>Deletes ended sessions and expired email codes, which would otherwise pile up forever.</summary>
public sealed partial class AccountCleanupJob(
    ISessionStore sessions,
    IAccountStore accounts,
    TimeProvider time,
    ILogger<AccountCleanupJob> logger) : IScheduledJob
{
    public const string JobName = "accounts.cleanup";

    /// <summary>Ended sessions are kept a little while, so a recent sign-out or revoke can still be looked into.</summary>
    public static readonly TimeSpan EndedSessionRetention = TimeSpan.FromDays(7);

    /// <summary>Small batches keep each delete short, so it never holds locks the site is waiting on.</summary>
    public const int BatchSize = 1000;

    public string Name => JobName;

    public TimeSpan Interval => TimeSpan.FromHours(1);

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow();
        var cutoff = now - EndedSessionRetention;
        var sessionCount = await DrainAsync(ct => sessions.DeleteEndedAsync(cutoff, BatchSize, ct), cancellationToken);
        var codeCount = await DrainAsync(ct => accounts.DeleteExpiredEmailCodesAsync(now, BatchSize, ct), cancellationToken);
        LogCleaned(logger, sessionCount, codeCount);
    }

    private static async Task<int> DrainAsync(Func<CancellationToken, Task<int>> deleteBatch, CancellationToken cancellationToken)
    {
        var total = 0;
        int deleted;
        do
        {
            deleted = await deleteBatch(cancellationToken);
            total += deleted;
        }
        while (deleted >= BatchSize);

        return total;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Account cleanup deleted {Sessions} ended sessions and {Codes} expired email codes")]
    private static partial void LogCleaned(ILogger logger, int sessions, int codes);
}
