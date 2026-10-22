using Beeps.Features.Shared;
using Beeps.Infrastructure.Data;
using Npgsql;

namespace Beeps.Infrastructure.Outbox;

/// <summary>Queues whose workers wait for a NOTIFY (<see cref="OutboxWorker"/>, Moderation.ModerationWorker, Translation.TranslationWorker).</summary>
[Flags]
public enum WorkQueues
{
    None = 0,
    Outbox = 1,
    Moderation = 2,
    Translation = 4,
}

/// <summary>
/// Wakes the workers on every instance (LISTEN) once new work is committed, with a NOTIFY of its own after the commit.
/// </summary>
/// <remarks>
/// <para>
/// Not from the writing transaction (the table triggers there were before): PostgreSQL commits every transaction that
/// notified one at a time, under one lock for the whole server held through the flush to disk, and nearly every write
/// (a post, a like, a follow) adds a message. That held the server to 4,000 such commits a second where it made 35,000
/// without them.
/// </para>
/// <para>
/// One NOTIFY per channel is on its way at a time; work committed meanwhile asks for one more after it, which is enough
/// for workers that drain everything due once woken. A NOTIFY that fails (database away) is only logged: the workers
/// also poll.
/// </para>
/// </remarks>
public sealed partial class WorkSignals(NpgsqlDataSource dataSource, ILogger<WorkSignals> logger)
{
    public const string OutboxChannel = "beeps_outbox";

    public const string ModerationChannel = "beeps_moderation";

    public const string TranslationChannel = "beeps_translation";

    private readonly LogThrottle notifyFailures = new();

    private readonly Lock gate = new();
    private WorkQueues sending;
    private WorkQueues again;

    /// <summary>Wakes the workers of <paramref name="queues"/>; call it once their new work is committed. Does not wait.</summary>
    public void Raise(WorkQueues queues)
    {
        foreach (var queue in (ReadOnlySpan<WorkQueues>)[WorkQueues.Outbox, WorkQueues.Moderation, WorkQueues.Translation])
        {
            if ((queues & queue) == 0)
            {
                continue;
            }

            lock (gate)
            {
                if ((sending & queue) != 0)
                {
                    again |= queue;
                    continue;
                }

                sending |= queue;
            }

            _ = SendAsync(queue);
        }
    }

    /// <summary>
    /// For work written outside the tracked changes (raw SQL), once it is written: it wakes the workers after the
    /// context's transaction commits, or now when there is none (the statement committed itself).
    /// </summary>
    public void Written(BeepsDbContext db, WorkQueues queues)
    {
        ArgumentNullException.ThrowIfNull(db);
        if (db.Database.CurrentTransaction is null)
        {
            Raise(queues);
        }
        else
        {
            db.WorkAfterCommit |= queues;
        }
    }

    private async Task SendAsync(WorkQueues queue)
    {
        var channel = queue switch
        {
            WorkQueues.Outbox => OutboxChannel,
            WorkQueues.Moderation => ModerationChannel,
            _ => TranslationChannel,
        };
        while (true)
        {
            try
            {
                await using var command = dataSource.CreateCommand("SELECT pg_notify($1, '')");
                command.Parameters.Add(new NpgsqlParameter { Value = channel });
                await command.ExecuteNonQueryAsync(CancellationToken.None);
            }
            catch (Exception exception)
            {
                if (notifyFailures.Allow())
                {
                    LogNotifyFailed(logger, exception, channel);
                }
            }

            lock (gate)
            {
                if ((again & queue) == 0)
                {
                    sending &= ~queue;
                    return;
                }

                again &= ~queue;
            }
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Waking the {Channel} workers failed; they find the work when they next poll (logged at most once a minute)")]
    private static partial void LogNotifyFailed(ILogger logger, Exception exception, string channel);
}
