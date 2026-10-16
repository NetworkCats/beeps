using System.Globalization;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;

namespace Beeps.Features.Shared.Http;

/// <summary>Writes an <see cref="Error"/> as ProblemDetails, including <c>Retry-After</c> for rate limits.</summary>
public sealed class ErrorHttpResult : IResult, IStatusCodeHttpResult, IValueHttpResult, IValueHttpResult<ProblemDetails>
{
    public ErrorHttpResult(Error error)
    {
        ArgumentNullException.ThrowIfNull(error);
        Error = error;
        StatusCode = StatusCodeFor(error.Kind);
        Value = CreateProblemDetails(error, StatusCode);
    }

    public Error Error { get; }

    public int StatusCode { get; }

    public ProblemDetails Value { get; }

    int? IStatusCodeHttpResult.StatusCode => StatusCode;

    object? IValueHttpResult.Value => Value;

    public static int StatusCodeFor(ErrorKind kind) => kind switch
    {
        ErrorKind.Validation => StatusCodes.Status400BadRequest,
        ErrorKind.Unauthorized => StatusCodes.Status401Unauthorized,
        ErrorKind.Forbidden => StatusCodes.Status403Forbidden,
        ErrorKind.NotFound => StatusCodes.Status404NotFound,
        ErrorKind.Conflict => StatusCodes.Status409Conflict,
        ErrorKind.RateLimited => StatusCodes.Status429TooManyRequests,
        ErrorKind.TooLarge => StatusCodes.Status413PayloadTooLarge,
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
    };

    /// <summary><c>Retry-After</c> in whole seconds, rounded up so a client never retries too early.</summary>
    public static string RetryAfterHeaderValue(TimeSpan retryAfter) =>
        Math.Max(1, (long)Math.Ceiling(retryAfter.TotalSeconds)).ToString(CultureInfo.InvariantCulture);

    public Task ExecuteAsync(HttpContext httpContext)
    {
        ArgumentNullException.ThrowIfNull(httpContext);
        if (Error.RetryAfter is { } retryAfter)
        {
            httpContext.Response.Headers.RetryAfter = RetryAfterHeaderValue(retryAfter);
        }

        return TypedResults.Problem(Value).ExecuteAsync(httpContext);
    }

    private static ProblemDetails CreateProblemDetails(Error error, int statusCode)
    {
        var problem = error.Fields is { } fields
            ? new HttpValidationProblemDetails(fields.ToDictionary(f => f.Key, f => f.Value))
            : new ProblemDetails();
        problem.Status = statusCode;
        problem.Detail = error.Detail;
        problem.Extensions[ProblemDetailsSetup.CodeKey] = error.Code;
        return problem;
    }
}

public static class ErrorHttpResultExtensions
{
    public static ErrorHttpResult ToHttpResult(this Error error) => new(error);
}
