namespace Beeps.Features.Shared;

/// <summary>
/// Commits what the feature stores (and <see cref="IOutbox"/>) have staged during the request. Stores
/// only stage changes; nothing is written until a Service calls <see cref="SaveChangesAsync"/>.
/// </summary>
public interface IUnitOfWork
{
    /// <exception cref="UniqueViolationException">A unique index rejected the change.</exception>
    Task SaveChangesAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Runs <paramref name="work"/> in one database transaction, holding the advisory lock
    /// <paramref name="lockKey"/> (see <see cref="AdvisoryLocks"/>) until it ends. The transaction commits
    /// only when the work succeeds; a failure result or an exception rolls everything back.
    /// </summary>
    Task<Result<T>> InTransactionAsync<T>(long? lockKey, Func<CancellationToken, Task<Result<T>>> work, CancellationToken cancellationToken);

    /// <inheritdoc cref="InTransactionAsync{T}"/>
    Task<Result> InTransactionAsync(long? lockKey, Func<CancellationToken, Task<Result>> work, CancellationToken cancellationToken);
}

/// <summary>A change was rejected by a unique index; <see cref="IndexName"/> says which (see the feature's index-name constants).</summary>
public sealed class UniqueViolationException : Exception
{
    public UniqueViolationException()
    {
    }

    public UniqueViolationException(string message)
        : base(message)
    {
    }

    public UniqueViolationException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    public string? IndexName { get; private init; }

    public static UniqueViolationException ForIndex(string? indexName, Exception innerException) =>
        new($"Unique index '{indexName}' rejected the change.", innerException) { IndexName = indexName };
}
