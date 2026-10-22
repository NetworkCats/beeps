using Beeps.Features.Translation;
using Beeps.Infrastructure.Data;
using Beeps.Infrastructure.Queues;
using Microsoft.Extensions.Options;
using Npgsql;

namespace Beeps.Infrastructure.Translation;

/// <summary>
/// Stages translations on the request's DbContext, so the next <c>SaveChangesAsync</c> commits them with the decision
/// that let the beep through; with translation off, nothing is queued. The workers are woken once it commits
/// (<see cref="Outbox.WorkSignalInterceptor"/>).
/// </summary>
public sealed class EfTranslationJobs(BeepsDbContext db, IOptions<TranslationOptions> options, TimeProvider time) : ITranslationJobs
{
    public void Enqueue(Guid beepId)
    {
        if (!options.Value.Enabled)
        {
            return;
        }

        var now = time.GetUtcNow();
        db.Set<TranslationJob>().Add(new TranslationJob { Id = Guid.CreateVersion7(now), BeepId = beepId, RunAfter = now, CreatedAt = now });
    }
}

/// <summary>The translation queue, as <see cref="TranslationWorker"/> takes from it.</summary>
public interface ITranslationJobStore : IBeepJobStore;

public sealed class PostgresTranslationJobStore(NpgsqlDataSource dataSource) : PostgresBeepJobStore(dataSource, "translation_jobs"), ITranslationJobStore;

/// <summary>Runs one claimed translation with retry: after the last attempt the beep stays as written.</summary>
public sealed class TranslationProcessor(
    ITranslationJobStore store,
    TranslationService translation,
    IOptions<TranslationOptions> options,
    TimeProvider time,
    ILogger<TranslationProcessor> logger) : BeepJobProcessor(store, options.Value, time, logger)
{
    protected override string Work => "Translation";

    protected override Task RunAsync(Guid beepId, CancellationToken cancellationToken) => translation.TranslateAsync(beepId, cancellationToken);

    protected override Task GiveUpAsync(Guid beepId, string error, CancellationToken cancellationToken)
    {
        translation.GiveUp(beepId, options.Value.MaxAttempts, error);
        return Task.CompletedTask;
    }
}

/// <summary>Translates the beeps waiting for it (<see cref="BeepJobWorker"/>), woken as they are let through.</summary>
public sealed class TranslationWorker(
    IServiceScopeFactory scopes,
    ITranslationJobStore store,
    NpgsqlDataSource dataSource,
    IOptions<TranslationOptions> options,
    TimeProvider time,
    ILogger<TranslationWorker> logger) : BeepJobWorker(scopes, store, dataSource, options.Value, options.Value.Enabled, time, logger)
{
    protected override string Queue => "Translation";

    protected override string Channel => Outbox.WorkSignals.TranslationChannel;

    protected override Task RunAsync(IServiceProvider services, ClaimedBeepJob job, CancellationToken stoppingToken) =>
        services.GetRequiredService<TranslationProcessor>().ProcessAsync(job, stoppingToken);
}
