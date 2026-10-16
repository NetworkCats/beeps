using System.Diagnostics.CodeAnalysis;

namespace Beeps.Features.Shared;

/// <summary>Outcome of a Service operation that has no value: success, or an <see cref="Shared.Error"/>.</summary>
public sealed class Result
{
    public static readonly Result Success = new(null);

    private Result(Error? error) => Error = error;

    public Error? Error { get; }

    [MemberNotNullWhen(false, nameof(Error))]
    public bool IsSuccess => Error is null;

    public static Result Failure(Error error) => new(error);

    public static implicit operator Result(Error error) => Failure(error);
}

/// <summary>Outcome of a Service operation: a value, or an <see cref="Shared.Error"/>. Services return this instead of throwing for expected failures.</summary>
public sealed class Result<T>
{
    private readonly T? value;

    private Result(T value)
    {
        this.value = value;
    }

    private Result(Error error)
    {
        Error = error;
    }

    public Error? Error { get; }

    [MemberNotNullWhen(false, nameof(Error))]
    public bool IsSuccess => Error is null;

    /// <summary>The value; only valid when <see cref="IsSuccess"/> is true.</summary>
    public T Value => IsSuccess ? value! : throw new InvalidOperationException($"Result is a failure ({Error.Code}); it has no value.");

    public static implicit operator Result<T>(T value) => new(value);

    public static implicit operator Result<T>(Error error) => new(error);

    public TOut Match<TOut>(Func<T, TOut> success, Func<Error, TOut> failure)
    {
        ArgumentNullException.ThrowIfNull(success);
        ArgumentNullException.ThrowIfNull(failure);
        return IsSuccess ? success(value!) : failure(Error);
    }
}
