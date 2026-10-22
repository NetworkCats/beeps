using Beeps.Features.Moderation;
using Beeps.Infrastructure.Data;
using Beeps.Infrastructure.Queues;
using Npgsql;

namespace Beeps.Infrastructure.Moderation;

/// <summary>
/// Stages reviews on the request's DbContext, so the next <c>SaveChangesAsync</c> commits them with the beep. The
/// workers are woken once it commits (<see cref="Outbox.WorkSignalInterceptor"/>).
/// </summary>
public sealed class EfModerationJobs(BeepsDbContext db, TimeProvider time) : IModerationJobs
{
    public void Enqueue(Guid beepId)
    {
        var now = time.GetUtcNow();
        db.Set<ModerationJob>().Add(new ModerationJob { Id = Guid.CreateVersion7(now), BeepId = beepId, RunAfter = now, CreatedAt = now });
    }
}

/// <summary>The review queue, as <see cref="ModerationWorker"/> takes from it.</summary>
public interface IModerationJobStore : IBeepJobStore;

public sealed class PostgresModerationJobStore(NpgsqlDataSource dataSource) : PostgresBeepJobStore(dataSource, "moderation_jobs"), IModerationJobStore;
