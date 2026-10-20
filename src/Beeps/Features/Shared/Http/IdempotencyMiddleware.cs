using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Beeps.Features.Auth;
using Microsoft.Extensions.Localization;
using Microsoft.Net.Http.Headers;

namespace Beeps.Features.Shared.Http;

/// <summary>Marks a write endpoint as accepting <c>Idempotency-Key</c>.</summary>
/// <param name="maxBodyBytes">Bodies up to this size are fingerprinted (in memory); larger ones run without replay protection.</param>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public sealed class IdempotentAttribute(int maxBodyBytes = IdempotencyMiddleware.MaxBodyLength) : Attribute
{
    public int MaxBodyBytes { get; } = maxBodyBytes;
}

public static class IdempotencyEndpointExtensions
{
    public static TBuilder WithIdempotency<TBuilder>(this TBuilder builder)
        where TBuilder : IEndpointConventionBuilder =>
        builder.WithMetadata(new IdempotentAttribute());

    /// <summary>For endpoints with bodies larger than JSON (image uploads).</summary>
    public static TBuilder WithIdempotency<TBuilder>(this TBuilder builder, int maxBodyBytes)
        where TBuilder : IEndpointConventionBuilder =>
        builder.WithMetadata(new IdempotentAttribute(maxBodyBytes));
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

    /// <summary>The default: beep text is at most a few KB; anything bigger is not fingerprinted in memory.</summary>
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
        var idempotent = context.GetEndpoint()?.Metadata.GetMetadata<IdempotentAttribute>();
        if (key.Length == 0
            || idempotent is null
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

        var fingerprint = await FingerprintAsync(context.Request, idempotent.MaxBodyBytes);
        if (fingerprint is null)
        {
            await next(context);
            return;
        }

        var storeKey = $"{context.User.ActorId():N}:{TextHashes.Sha256Hex(key)}";
        IdempotencyRecord? existing;
        try
        {
            existing = await store.TryClaimAsync(storeKey, fingerprint, ClaimDuration, context.RequestAborted);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // The store is PostgreSQL, which the write itself needs too; this only lets a write through that would
            // most likely fail on its own anyway.
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

    /// <summary>
    /// A hash of the method, path, query and raw body bytes; null when the body is larger than
    /// <paramref name="maxBodyBytes"/>. The body stays buffered in memory (never on disk) for the endpoint.
    /// </summary>
    private static async Task<string?> FingerprintAsync(HttpRequest request, int maxBodyBytes)
    {
        if (request.ContentLength > maxBodyBytes)
        {
            return null;
        }

        var chunk = new byte[16 * 1024];
        // Room for the one chunk read past the limit, so nothing is ever buffered to a temporary file.
        request.EnableBuffering(bufferThreshold: maxBodyBytes + chunk.Length);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hash.AppendData(Encoding.UTF8.GetBytes($"{request.Method}\n{request.Path}{request.QueryString}\n"));
        long total = 0;
        int read;
        while ((read = await request.Body.ReadAsync(chunk, request.HttpContext.RequestAborted)) > 0)
        {
            total += read;
            if (total > maxBodyBytes)
            {
                request.Body.Position = 0;
                return null;
            }

            hash.AppendData(chunk, 0, read);
        }

        request.Body.Position = 0;
        return Convert.ToHexStringLower(hash.GetHashAndReset());
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
        var aborted = context.RequestAborted;
        using var buffer = new MemoryStream();
        response.Body = buffer;
        // The write runs to its end even when the caller goes away (a client timeout): one that stopped after its
        // commit would otherwise be released as failed, and the retry with the same key would write it again.
        context.RequestAborted = CancellationToken.None;
        var succeeded = false;
        try
        {
            await next(context);
            succeeded = response.StatusCode is >= 200 and < 300;
        }
        finally
        {
            context.RequestAborted = aborted;
            response.Body = original;
            await RememberAsync(store, storeKey, succeeded ? Record(response, fingerprint, buffer) : null);
        }

        buffer.Position = 0;
        await buffer.CopyToAsync(original, aborted);
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
