using System.Security.Claims;
using System.Text;
using Beeps.Features.Auth;
using Microsoft.Extensions.Localization;
using Microsoft.Net.Http.Headers;

namespace Beeps.Features.Shared.Http;

/// <summary>Marks a write endpoint as accepting <c>Idempotency-Key</c>.</summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public sealed class IdempotentAttribute : Attribute;

public static class IdempotencyEndpointExtensions
{
    public static TBuilder WithIdempotency<TBuilder>(this TBuilder builder)
        where TBuilder : IEndpointConventionBuilder =>
        builder.WithMetadata(new IdempotentAttribute());
}

public static class IdempotencyErrorCodes
{
    public const string KeyInvalid = "idempotency_key_invalid";
    public const string KeyInUse = "idempotency_key_in_use";
    public const string KeyReused = "idempotency_key_reused";
}

/// <summary>
/// <c>Idempotency-Key</c> for write endpoints (PLAN 12). The first request with a key runs and, if it
/// succeeds, its response is kept for a day; a retry with the same key and body gets that response again
/// (marked <c>Idempotent-Replayed: true</c>) without running. Keys are per caller. Failures are not kept,
/// so a request that failed can be retried with the same key.
/// </summary>
public sealed partial class IdempotencyMiddleware(RequestDelegate next, ILogger<IdempotencyMiddleware> logger)
{
    public const string HeaderName = "Idempotency-Key";

    public const string ReplayedHeaderName = "Idempotent-Replayed";

    public const int MaxKeyLength = 255;

    /// <summary>Beep text is at most a few KB; anything bigger is not fingerprinted in memory.</summary>
    public const int MaxBodyLength = 64 * 1024;

    public static readonly TimeSpan Retention = TimeSpan.FromHours(24);

    /// <summary>How long a claim blocks a concurrent retry; longer than any write takes.</summary>
    public static readonly TimeSpan ClaimDuration = TimeSpan.FromMinutes(1);

    public async Task InvokeAsync(HttpContext context, IIdempotencyStore store, IStringLocalizer<SharedResource> localizer)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(localizer);
        var key = context.Request.Headers[HeaderName].ToString();
        if (key.Length == 0
            || context.GetEndpoint()?.Metadata.GetMetadata<IdempotentAttribute>() is null
            || context.User.FindFirstValue(ClaimTypes.NameIdentifier) is null)
        {
            await next(context);
            return;
        }

        if (key.Length > MaxKeyLength || !key.All(c => c is >= '!' and <= '~'))
        {
            await Error.Validation(IdempotencyErrorCodes.KeyInvalid, localizer["Error.IdempotencyKeyInvalid", MaxKeyLength]).ToHttpResult().ExecuteAsync(context);
            return;
        }

        var body = await ReadBodyAsync(context.Request);
        if (body is null)
        {
            await next(context);
            return;
        }

        var storeKey = $"{context.User.ActorId():N}:{TextHashes.Sha256Hex(key)}";
        var fingerprint = TextHashes.Sha256Hex($"{context.Request.Method}\n{context.Request.Path}{context.Request.QueryString}\n{body}");
        IdempotencyRecord? existing;
        try
        {
            existing = await store.TryClaimAsync(storeKey, fingerprint, ClaimDuration, context.RequestAborted);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // Same trade-off as rate limiting: a Valkey outage must not stop all writes.
            LogStoreFailed(logger, exception);
            await next(context);
            return;
        }

        if (existing is not null)
        {
            await AnswerExistingAsync(context, existing, fingerprint, localizer);
            return;
        }

        await RunAndRememberAsync(context, store, storeKey, fingerprint);
    }

    private static async Task<string?> ReadBodyAsync(HttpRequest request)
    {
        if (request.ContentLength > MaxBodyLength)
        {
            return null;
        }

        request.EnableBuffering(MaxBodyLength);
        using var reader = new StreamReader(request.Body, Encoding.UTF8, detectEncodingFromByteOrderMarks: false, leaveOpen: true);
        var body = await reader.ReadToEndAsync(request.HttpContext.RequestAborted);
        request.Body.Position = 0;
        return body.Length > MaxBodyLength ? null : body;
    }

    private static Task AnswerExistingAsync(HttpContext context, IdempotencyRecord existing, string fingerprint, IStringLocalizer<SharedResource> localizer)
    {
        if (!TextHashes.FixedTimeEquals(existing.Fingerprint, fingerprint))
        {
            return Error.Validation(IdempotencyErrorCodes.KeyReused, localizer["Error.IdempotencyKeyReused"]).ToHttpResult().ExecuteAsync(context);
        }

        if (existing.IsPending)
        {
            return Error.Conflict(IdempotencyErrorCodes.KeyInUse, localizer["Error.IdempotencyKeyInUse"]).ToHttpResult().ExecuteAsync(context);
        }

        var response = context.Response;
        response.StatusCode = existing.StatusCode!.Value;
        response.Headers[ReplayedHeaderName] = "true";
        if (existing.Location is not null)
        {
            response.Headers.Location = existing.Location;
        }

        if (existing.ContentType is not null)
        {
            response.ContentType = existing.ContentType;
        }

        return existing.Body is { Length: > 0 } body ? response.WriteAsync(body, context.RequestAborted) : Task.CompletedTask;
    }

    private async Task RunAndRememberAsync(HttpContext context, IIdempotencyStore store, string storeKey, string fingerprint)
    {
        var response = context.Response;
        var original = response.Body;
        using var buffer = new MemoryStream();
        response.Body = buffer;
        var succeeded = false;
        try
        {
            await next(context);
            succeeded = response.StatusCode is >= 200 and < 300;
        }
        finally
        {
            response.Body = original;
            await RememberAsync(store, storeKey, succeeded ? Record(response, fingerprint, buffer) : null);
        }

        buffer.Position = 0;
        await buffer.CopyToAsync(original, context.RequestAborted);
    }

    private static IdempotencyRecord Record(HttpResponse response, string fingerprint, MemoryStream buffer) => new(
        fingerprint,
        response.StatusCode,
        response.ContentType,
        response.Headers[HeaderNames.Location].ToString() is { Length: > 0 } location ? location : null,
        Encoding.UTF8.GetString(buffer.GetBuffer(), 0, (int)buffer.Length));

    // Runs even when the request was aborted: the claim must not block retries for its whole duration.
    private async Task RememberAsync(IIdempotencyStore store, string storeKey, IdempotencyRecord? record)
    {
        try
        {
            if (record is null)
            {
                await store.ReleaseAsync(storeKey, CancellationToken.None);
            }
            else
            {
                await store.CompleteAsync(storeKey, record, Retention, CancellationToken.None);
            }
        }
        catch (Exception exception)
        {
            LogStoreFailed(logger, exception);
        }
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Idempotency store failed; the request runs without replay protection")]
    private static partial void LogStoreFailed(ILogger logger, Exception exception);
}
