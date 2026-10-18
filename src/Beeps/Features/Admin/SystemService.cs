using Beeps.Features.Moderation;
using Beeps.Features.Shared;
using Microsoft.Extensions.Localization;

namespace Beeps.Features.Admin;

/// <param name="Since">Start of "today": midnight UTC, as the backend knows no other time zone (PLAN 6.17).</param>
/// <param name="ReviewQueue">Beeps waiting for an admin (hidden, failed review, reported).</param>
/// <param name="ModerationBacklog">Beeps waiting for their AI review.</param>
/// <param name="OutboxBacklog">Background jobs waiting or being retried.</param>
public sealed record AdminOverview(
    DateTimeOffset Since,
    int Users,
    int Agents,
    int Beeps,
    int NewUsers,
    int NewAgents,
    int NewBeeps,
    int ReviewQueue,
    int ModerationBacklog,
    int OutboxBacklog,
    int DeadJobs);

/// <summary>The counts the overview shows, besides the review queue.</summary>
/// <summary>See <see cref="ISystemStore.CountUnannouncedAsync"/>.</summary>
public sealed record UnannouncedCounts(int NewUsers, int ModerationBacklog, int OutboxBacklog, int DeadJobs);

public sealed record SiteCounts(int Users, int Agents, int Beeps, int NewUsers, int NewAgents, int NewBeeps, int ModerationBacklog, int OutboxBacklog, int DeadJobs);

/// <summary>An outbox message that ran out of attempts. Its payload is not shown: it can hold email addresses.</summary>
public sealed record DeadJobRow(Guid Id, string Type, int Attempts, string LastError, DateTimeOffset CreatedAt, DateTimeOffset FailedAt);

public sealed record ScheduledJobRow(string Name, DateTimeOffset? LastStartedAt, DateTimeOffset? LastFinishedAt, string? LastError);

/// <summary>Counts and background-job bookkeeping for the admin area. Writes run immediately; call them inside a transaction.</summary>
public interface ISystemStore
{
    /// <param name="since">For the "new" counts.</param>
    Task<SiteCounts> CountAsync(DateTimeOffset since, CancellationToken cancellationToken);

    /// <summary>
    /// The figures of <see cref="CountAsync"/> that change with no event of their own (beeps and agents have theirs), for
    /// watching them every few seconds: new sign-ups and the backlogs.
    /// </summary>
    Task<UnannouncedCounts> CountUnannouncedAsync(DateTimeOffset since, CancellationToken cancellationToken);

    /// <summary>Newest failure first.</summary>
    Task<CursorPage<DeadJobRow>> ListDeadJobsAsync(Guid? before, int limit, CancellationToken cancellationToken);

    /// <summary>Moves a dead job back into the outbox with fresh attempts, due now. False if there is no such dead job.</summary>
    Task<bool> RetryDeadJobAsync(Guid id, DateTimeOffset now, CancellationToken cancellationToken);

    /// <summary>False if there is no such dead job.</summary>
    Task<bool> DeleteDeadJobAsync(Guid id, CancellationToken cancellationToken);

    Task<IReadOnlyList<ScheduledJobRow>> ListScheduledJobsAsync(CancellationToken cancellationToken);
}

/// <summary>The overview and the "system" page of the admin area (PLAN 3.6): backlogs, dead jobs, scheduled jobs.</summary>
public sealed partial class SystemService(
    ISystemStore store,
    IModerationStore moderation,
    AdminAccess access,
    IUnitOfWork unitOfWork,
    TimeProvider time,
    IStringLocalizer<SharedResource> localizer,
    ILogger<SystemService> logger)
{
    public const int PageSize = 50;

    public async Task<Result<AdminOverview>> GetOverviewAsync(Guid adminId, CancellationToken cancellationToken)
    {
        if (await access.RequireAdminAsync(adminId, cancellationToken) is { } denied)
        {
            return denied;
        }

        var since = new DateTimeOffset(time.GetUtcNow().UtcDateTime.Date, TimeSpan.Zero);
        var counts = await store.CountAsync(since, cancellationToken);
        var queue = await moderation.CountQueueAsync(cancellationToken);
        return new AdminOverview(
            since,
            counts.Users,
            counts.Agents,
            counts.Beeps,
            counts.NewUsers,
            counts.NewAgents,
            counts.NewBeeps,
            queue,
            counts.ModerationBacklog,
            counts.OutboxBacklog,
            counts.DeadJobs);
    }

    public async Task<Result<CursorPage<DeadJobRow>>> ListDeadJobsAsync(Guid adminId, Guid? before, CancellationToken cancellationToken) =>
        await access.RequireAdminAsync(adminId, cancellationToken) is { } denied
            ? denied
            : await store.ListDeadJobsAsync(before, PageSize, cancellationToken);

    public async Task<Result<IReadOnlyList<ScheduledJobRow>>> ListScheduledJobsAsync(Guid adminId, CancellationToken cancellationToken) =>
        await access.RequireAdminAsync(adminId, cancellationToken) is { } denied
            ? denied
            : Result.Of(await store.ListScheduledJobsAsync(cancellationToken));

    /// <summary>Gives a dead job a fresh set of attempts (PLAN 3.6), once whatever made it fail is fixed.</summary>
    public Task<Result> RetryDeadJobAsync(Guid adminId, Guid jobId, CancellationToken cancellationToken) =>
        ChangeDeadJobAsync(adminId, jobId, AuditActions.RetryJob, ct => store.RetryDeadJobAsync(jobId, time.GetUtcNow(), ct), cancellationToken);

    /// <summary>Drops a dead job for good (PLAN 3.6).</summary>
    public Task<Result> DiscardDeadJobAsync(Guid adminId, Guid jobId, CancellationToken cancellationToken) =>
        ChangeDeadJobAsync(adminId, jobId, AuditActions.DiscardJob, ct => store.DeleteDeadJobAsync(jobId, ct), cancellationToken);

    private async Task<Result> ChangeDeadJobAsync(Guid adminId, Guid jobId, string action, Func<CancellationToken, Task<bool>> change, CancellationToken cancellationToken)
    {
        if (await access.RequireAdminAsync(adminId, cancellationToken) is { } denied)
        {
            return denied;
        }

        var result = await unitOfWork.InTransactionAsync(
            null,
            async ct =>
            {
                if (!await change(ct))
                {
                    return Error.NotFound(ErrorCodes.NotFound, localizer["Error.JobNotFound"]);
                }

                access.Audit(adminId, action, AuditTargets.Job, jobId, null);
                await unitOfWork.SaveChangesAsync(ct);
                return Result.Success;
            },
            cancellationToken);
        if (result.IsSuccess)
        {
            LogJobChanged(logger, jobId, action, adminId);
        }

        return result;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Dead job {JobId}: {Action} by {AdminId}")]
    private static partial void LogJobChanged(ILogger logger, Guid jobId, string action, Guid adminId);
}
