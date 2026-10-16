using Beeps.Features.Shared;

namespace Beeps.Tests.Support;

/// <summary>
/// Runs transactional work inline and records what happened, so Service tests can assert on saves,
/// lock keys and rollbacks without a database.
/// </summary>
internal sealed class ImmediateUnitOfWork : IUnitOfWork
{
    public int Saves { get; private set; }

    public List<long?> Locks { get; } = [];

    public int Rollbacks { get; private set; }

    /// <summary>When set, the next save throws it (e.g. a unique index losing a race).</summary>
    public Exception? FailNextSave { get; set; }

    public Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        if (FailNextSave is { } failure)
        {
            FailNextSave = null;
            throw failure;
        }

        Saves++;
        return Task.CompletedTask;
    }

    public async Task<Result<T>> InTransactionAsync<T>(long? lockKey, Func<CancellationToken, Task<Result<T>>> work, CancellationToken cancellationToken)
    {
        Locks.Add(lockKey);
        var result = await work(cancellationToken);
        Rollbacks += result.IsSuccess ? 0 : 1;
        return result;
    }

    public async Task<Result> InTransactionAsync(long? lockKey, Func<CancellationToken, Task<Result>> work, CancellationToken cancellationToken)
    {
        Locks.Add(lockKey);
        var result = await work(cancellationToken);
        Rollbacks += result.IsSuccess ? 0 : 1;
        return result;
    }
}
