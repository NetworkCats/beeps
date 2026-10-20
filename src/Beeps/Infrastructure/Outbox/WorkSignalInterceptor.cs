using System.Data.Common;
using Beeps.Features.Moderation;
using Beeps.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Beeps.Infrastructure.Outbox;

/// <summary>
/// Wakes the workers (<see cref="WorkSignals"/>) when a save adds outbox messages or reviews: once the save is done when
/// it commits itself, or once the transaction it is part of commits. A rolled-back transaction wakes no one.
/// </summary>
public sealed class WorkSignalInterceptor(WorkSignals signals) : DbTransactionInterceptor, ISaveChangesInterceptor
{
    public ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        Saving(eventData);
        return ValueTask.FromResult(result);
    }

    public InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        Saving(eventData);
        return result;
    }

    public ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData eventData, int result, CancellationToken cancellationToken = default)
    {
        Saved(eventData);
        return ValueTask.FromResult(result);
    }

    public int SavedChanges(SaveChangesCompletedEventData eventData, int result)
    {
        Saved(eventData);
        return result;
    }

    public Task SaveChangesFailedAsync(DbContextErrorEventData eventData, CancellationToken cancellationToken = default)
    {
        Failed(eventData);
        return Task.CompletedTask;
    }

    public void SaveChangesFailed(DbContextErrorEventData eventData) => Failed(eventData);

    public override ValueTask<DbTransaction> TransactionStartedAsync(DbConnection connection, TransactionEndEventData eventData, DbTransaction result, CancellationToken cancellationToken = default)
    {
        Started(eventData);
        return ValueTask.FromResult(result);
    }

    public override DbTransaction TransactionStarted(DbConnection connection, TransactionEndEventData eventData, DbTransaction result)
    {
        Started(eventData);
        return result;
    }

    public override Task TransactionCommittedAsync(DbTransaction transaction, TransactionEndEventData eventData, CancellationToken cancellationToken = default)
    {
        Committed(eventData);
        return Task.CompletedTask;
    }

    public override void TransactionCommitted(DbTransaction transaction, TransactionEndEventData eventData) => Committed(eventData);

    public override Task TransactionRolledBackAsync(DbTransaction transaction, TransactionEndEventData eventData, CancellationToken cancellationToken = default)
    {
        Ended(eventData);
        return Task.CompletedTask;
    }

    public override void TransactionRolledBack(DbTransaction transaction, TransactionEndEventData eventData) => Ended(eventData);

    private static void Saving(DbContextEventData eventData)
    {
        if (eventData.Context is not BeepsDbContext db)
        {
            return;
        }

        var added = WorkQueues.None;
        foreach (var entry in db.ChangeTracker.Entries())
        {
            if (entry.State == EntityState.Added)
            {
                added |= entry.Entity switch
                {
                    OutboxMessage => WorkQueues.Outbox,
                    ModerationJob => WorkQueues.Moderation,
                    _ => WorkQueues.None,
                };
            }
        }

        if (db.Database.CurrentTransaction is null)
        {
            db.WorkAfterSave |= added;
        }
        else
        {
            db.WorkAfterCommit |= added;
        }
    }

    private void Saved(SaveChangesCompletedEventData eventData)
    {
        if (eventData.Context is BeepsDbContext { WorkAfterSave: not WorkQueues.None and var queues } db)
        {
            db.WorkAfterSave = WorkQueues.None;
            signals.Raise(queues);
        }
    }

    private static void Failed(DbContextEventData eventData)
    {
        if (eventData.Context is BeepsDbContext db)
        {
            db.WorkAfterSave = WorkQueues.None;
        }
    }

    // Work staged before a transaction is the save's own (it commits itself) or was signalled already: nothing left
    // over from a transaction disposed without a rollback carries into this one.
    private static void Started(TransactionEndEventData eventData)
    {
        if (eventData.Context is BeepsDbContext db)
        {
            db.WorkAfterCommit = WorkQueues.None;
        }
    }

    private void Committed(TransactionEndEventData eventData)
    {
        if (eventData.Context is BeepsDbContext { WorkAfterCommit: not WorkQueues.None and var queues } db)
        {
            db.WorkAfterCommit = WorkQueues.None;
            signals.Raise(queues);
        }
    }

    private static void Ended(TransactionEndEventData eventData)
    {
        if (eventData.Context is BeepsDbContext db)
        {
            db.WorkAfterCommit = WorkQueues.None;
        }
    }
}
