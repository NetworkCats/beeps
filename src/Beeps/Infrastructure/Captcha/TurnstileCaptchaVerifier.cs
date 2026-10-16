using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Beeps.Features.Accounts;
using Microsoft.Extensions.Options;

namespace Beeps.Infrastructure.Captcha;

/// <summary>Cloudflare Turnstile server-side validation (PLAN 6.15). Fails closed: if Cloudflare cannot be asked, the answer is no.</summary>
public sealed partial class TurnstileCaptchaVerifier(
    HttpClient http,
    IOptions<CaptchaOptions> options,
    ILogger<TurnstileCaptchaVerifier> logger) : ICaptchaVerifier
{
    public static readonly Uri SiteVerifyUri = new("https://challenges.cloudflare.com/turnstile/v0/siteverify");

    // Turnstile tokens are at most 2048 characters.
    private const int MaxTokenLength = 2048;

    public async Task<bool> VerifyAsync(string? token, string clientIp, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(token) || token.Length > MaxTokenLength)
        {
            return false;
        }

        using var form = new FormUrlEncodedContent(
        [
            KeyValuePair.Create("secret", options.Value.SecretKey),
            KeyValuePair.Create("response", token),
            KeyValuePair.Create("remoteip", clientIp),
        ]);
        try
        {
            using var response = await http.PostAsync(SiteVerifyUri, form, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                LogUnavailable(logger, (int)response.StatusCode);
                return false;
            }

            var result = await response.Content.ReadFromJsonAsync(TurnstileJsonContext.Default.TurnstileResponse, cancellationToken);
            if (result?.Success != true)
            {
                LogRejected(logger, result?.ErrorCodes);
                return false;
            }

            return true;
        }
        catch (Exception exception) when (exception is HttpRequestException or JsonException
            || (exception is TaskCanceledException && !cancellationToken.IsCancellationRequested))
        {
            LogFailed(logger, exception);
            return false;
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Turnstile answered HTTP {StatusCode}; rejecting the token")]
    private static partial void LogUnavailable(ILogger logger, int statusCode);

    [LoggerMessage(Level = LogLevel.Information, Message = "Turnstile rejected a token: {ErrorCodes}")]
    private static partial void LogRejected(ILogger logger, string[]? errorCodes);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Turnstile could not be reached; rejecting the token")]
    private static partial void LogFailed(ILogger logger, Exception exception);
}

internal sealed record TurnstileResponse(
    [property: JsonPropertyName("success")] bool Success,
    [property: JsonPropertyName("error-codes")] string[]? ErrorCodes);

[JsonSerializable(typeof(TurnstileResponse))]
internal sealed partial class TurnstileJsonContext : JsonSerializerContext;
