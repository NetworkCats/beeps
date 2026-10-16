using Beeps.Features.Shared;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Beeps.Infrastructure.Data;

/// <summary>Commits the request's DbContext, which every store and the outbox stage their changes on.</summary>
public sealed class EfUnitOfWork(BeepsDbContext db) : IUnitOfWork
{
    public async Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation } postgres)
        {
            throw UniqueViolationException.ForIndex(postgres.ConstraintName, exception);
        }
    }

    public async Task<Result<T>> InTransactionAsync<T>(long? lockKey, Func<CancellationToken, Task<Result<T>>> work, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(work);
        await using var transaction = await BeginAsync(lockKey, cancellationToken);
        var result = await work(cancellationToken);
        await EndAsync(transaction, result.IsSuccess, cancellationToken);
        return result;
    }

    public async Task<Result> InTransactionAsync(long? lockKey, Func<CancellationToken, Task<Result>> work, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(work);
        await using var transaction = await BeginAsync(lockKey, cancellationToken);
        var result = await work(cancellationToken);
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
            await transaction.CommitAsync(cancellationToken);
            return;
        }

        await transaction.RollbackAsync(cancellationToken);
        // What was staged belonged to the rolled-back work; it must not leak into a later save.
        db.ChangeTracker.Clear();
    }
}
