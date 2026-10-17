using Beeps.Features.Shared;
using Beeps.Infrastructure.Caching;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Beeps.Infrastructure.Data;

/// <summary>
/// Commits the request's DbContext, which every store and the outbox stage their changes on, and hands the cache
/// invalidations of what it committed (<see cref="PendingCacheChanges"/>) on once it has.
/// </summary>
public sealed class EfUnitOfWork(BeepsDbContext db, PendingCacheChanges cacheChanges) : IUnitOfWork
{
    public async Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        cacheChanges.CaptureTracked();
        var outsideTransaction = db.Database.CurrentTransaction is null;
        if (outsideTransaction)
        {
            // Saved in the same statement batch as the change; inside a transaction it is staged before the commit.
            cacheChanges.StageDurableCopy();
        }

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation } postgres)
        {
            DiscardOutsideTransaction();
            throw UniqueViolationException.ForIndex(postgres.ConstraintName, exception);
        }
        catch
        {
            DiscardOutsideTransaction();
            throw;
        }

        // Inside a transaction they wait for the commit.
        if (outsideTransaction)
        {
            cacheChanges.Flush();
        }
    }

    public async Task<Result<T>> InTransactionAsync<T>(long? lockKey, Func<CancellationToken, Task<Result<T>>> work, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(work);
        await using var transaction = await BeginAsync(lockKey, cancellationToken);
        Result<T> result;
        try
        {
            result = await work(cancellationToken);
        }
        catch
        {
            // Disposing the transaction rolls it back.
            cacheChanges.Discard();
            throw;
        }

        await EndAsync(transaction, result.IsSuccess, cancellationToken);
        return result;
    }

    public async Task<Result> InTransactionAsync(long? lockKey, Func<CancellationToken, Task<Result>> work, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(work);
        await using var transaction = await BeginAsync(lockKey, cancellationToken);
        Result result;
        try
        {
            result = await work(cancellationToken);
        }
        catch
        {
            cacheChanges.Discard();
            throw;
        }

        await EndAsync(transaction, result.IsSuccess, cancellationToken);
        return result;
    }

    private async Task<Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction> BeginAsync(long? lockKey, CancellationToken cancellationToken)
    {
        var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        if (lockKey is { } key)
        {
            // Released automatically when the transaction ends, however it ends.
            await db.Database.ExecuteSqlAsync($"SELECT pg_advisory_xact_lock({key})", cancellationToken);
        }

        return transaction;
    }

    private async Task EndAsync(Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction transaction, bool commit, CancellationToken cancellationToken)
    {
        if (commit)
        {
            // Writes the stores ran directly, and tracked changes left unsaved, have their durable copy committed too.
            cacheChanges.CaptureTracked();
            if (cacheChanges.HasPending)
            {
                cacheChanges.StageDurableCopy();
                try
                {
                    await db.SaveChangesAsync(cancellationToken);
                }
                catch
                {
                    // Disposing the transaction rolls it back.
                    cacheChanges.Discard();
                    throw;
                }
            }

            try
            {
                await transaction.CommitAsync(cancellationToken);
            }
            catch
            {
                // It may have committed all the same; dropping a fresh entry is harmless, keeping a stale one is not.
                cacheChanges.Flush();
                throw;
            }

            cacheChanges.Flush();
            return;
        }

        cacheChanges.Discard();
        await transaction.RollbackAsync(cancellationToken);
        // What was staged belonged to the rolled-back work; it must not leak into a later save.
        db.ChangeTracker.Clear();
    }

    private void DiscardOutsideTransaction()
    {
        if (db.Database.CurrentTransaction is null)
        {
            cacheChanges.Discard();
        }
    }
}
