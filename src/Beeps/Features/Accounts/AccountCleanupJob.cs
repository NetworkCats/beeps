using Beeps.Features.Live;
using Beeps.Features.Shared;

namespace Beeps.Features.Accounts;

/// <summary>
/// Deletes ended sessions and expired email codes, which would otherwise pile up forever, and releases accounts that
/// never verified their email, which would otherwise hold their email and username forever.
/// </summary>
public sealed partial class AccountCleanupJob(
    ISessionStore sessions,
    IAccountStore accounts,
    IUnitOfWork unitOfWork,
    LiveUpdates live,
    TimeProvider time,
    ILogger<AccountCleanupJob> logger) : IScheduledJob
{
    /// <summary>
    /// How long a sign-up may stay unverified. Until then its email and username are taken, so without a limit anyone
    /// could register someone else's address, never verify, and keep its owner from ever signing up.
    /// </summary>
    public static readonly TimeSpan UnverifiedRetention = TimeSpan.FromDays(7);

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
        var released = await DrainAsync(ct => ReleaseUnverifiedAsync(now, ct), cancellationToken);
        LogCleaned(logger, sessionCount, codeCount, released);
    }

    private async Task<int> ReleaseUnverifiedAsync(DateTimeOffset now, CancellationToken cancellationToken)
    {
        var released = await unitOfWork.InTransactionAsync<IReadOnlyList<Guid>>(
            null,
            async ct => Result.Of(await accounts.ReleaseUnverifiedAsync(now - UnverifiedRetention, now, BatchSize, ct)),
            cancellationToken);
        // A sign-up mid-onboarding is signed in: its sessions end now, not when their cached copies expire.
        await sessions.ForgetUsersAsync(released.Value, cancellationToken);
        await accounts.ForgetAccountsAsync(released.Value, cancellationToken);
        foreach (var userId in released.Value)
        {
            // Its open tabs sign out.
            await live.SessionsEndedAsync(userId, cancellationToken);
        }

        return released.Value.Count;
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

    [LoggerMessage(Level = LogLevel.Information, Message = "Account cleanup deleted {Sessions} ended sessions and {Codes} expired email codes, and released {Unverified} unverified accounts")]
    private static partial void LogCleaned(ILogger logger, int sessions, int codes, int unverified);
}
