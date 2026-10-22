using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Beeps.Infrastructure.OpenRouter;

public sealed class OpenRouterOptions
{
    public const string SectionName = "OpenRouter";

    /// <summary>Server-side only; required while moderation or translation is enabled outside development.</summary>
    public string ApiKey { get; set; } = "";

    public Uri BaseUrl { get; set; } = new("https://openrouter.ai/api/v1/");
}

/// <summary>The model's answer, with who gave it and what it cost.</summary>
/// <param name="Content">The message, still to be checked against the schema; null when there was none.</param>
/// <param name="Tokens">Prompt and completion tokens together; null when the response does not say.</param>
public sealed record ChatAnswer(string? Content, string Model, string? Provider, int LatencyMs, int? Tokens);

/// <summary>
/// One question to OpenRouter's OpenAI-compatible chat completions, called with a plain HttpClient: the instructions
/// and the data in messages of their own, structured output with a strict schema, routed to the fastest provider that
/// supports it and runs the model at full enough precision (<see cref="Quantizations"/>). Every failure (network, timeout, an error status or response) throws what <c>fail</c> makes of it.
/// </summary>
internal static class OpenRouterChat
{
    /// <summary>
    /// The precisions a provider may run the model at. Many serve it cut down to 4 bits (fp4, mxfp4, int4), or do not say
    /// (unknown), and answer noticeably worse: they are never asked.
    /// </summary>
    public static readonly IReadOnlyList<string> Quantizations = ["fp8", "fp16", "bf16", "fp32"];

    public static async Task<ChatAnswer> AskAsync(
        HttpClient http,
        TimeProvider time,
        string model,
        string systemPrompt,
        string content,
        string schemaName,
        JsonElement schema,
        Func<string, Exception?, Exception> fail,
        CancellationToken cancellationToken)
    {
        var request = new ChatRequest(
            model,
            [new ChatRequestMessage("system", systemPrompt), new ChatRequestMessage("user", content)],
            new ResponseFormat("json_schema", new JsonSchemaFormat(schemaName, true, schema)),
            new ProviderPreferences("throughput", true, Quantizations),
            0);

        var started = time.GetTimestamp();
        ChatResponse response;
        try
        {
            using var message = new HttpRequestMessage(HttpMethod.Post, "chat/completions")
            {
                Content = new StringContent(JsonSerializer.Serialize(request, OpenRouterJsonContext.Default.ChatRequest), Encoding.UTF8, "application/json"),
            };
            using var reply = await http.SendAsync(message, cancellationToken);
            var body = await reply.Content.ReadAsStringAsync(cancellationToken);
            if (!reply.IsSuccessStatusCode)
            {
                throw fail($"OpenRouter answered {(int)reply.StatusCode}: {Excerpt(body)}", null);
            }

            response = JsonSerializer.Deserialize(body, OpenRouterJsonContext.Default.ChatResponse)
                ?? throw fail("OpenRouter sent an empty response.", null);
        }
        catch (Exception exception) when (exception is HttpRequestException or JsonException
            || (exception is OperationCanceledException && !cancellationToken.IsCancellationRequested))
        {
            // A cancellation that is not ours is the client's timeout.
            throw fail($"OpenRouter request failed: {exception.GetType().Name}: {exception.Message}", exception);
        }

        var latency = (int)Math.Min(int.MaxValue, time.GetElapsedTime(started).TotalMilliseconds);
        if (response.Error is { } error)
        {
            throw fail($"OpenRouter reported an error: {error.Code} {Excerpt(error.Message)}", null);
        }

        return new ChatAnswer(
            response.Choices is [{ Message.Content: { } text }, ..] ? text : null,
            response.Model ?? model,
            response.Provider,
            latency,
            response.Usage?.TotalTokens ?? (response.Usage is { PromptTokens: { } prompt, CompletionTokens: { } completion } ? prompt + completion : null));
    }

    public static string Excerpt(string? text) => text is null ? "" : text.Length <= 300 ? text : text[..300];
}

internal sealed record ChatRequest(
    string Model,
    IReadOnlyList<ChatRequestMessage> Messages,
    ResponseFormat ResponseFormat,
    ProviderPreferences Provider,
    double Temperature);

internal sealed record ChatRequestMessage(string Role, string Content);

internal sealed record ResponseFormat(string Type, JsonSchemaFormat JsonSchema);

internal sealed record JsonSchemaFormat(string Name, bool Strict, JsonElement Schema);

/// <summary>The fastest provider, and only ones that honour the structured output and run the model at one of <paramref name="Quantizations"/>.</summary>
internal sealed record ProviderPreferences(string Sort, bool RequireParameters, IReadOnlyList<string> Quantizations);

internal sealed record ChatResponse(string? Model, string? Provider, IReadOnlyList<ChatChoice>? Choices, ChatUsage? Usage, ChatError? Error);

internal sealed record ChatChoice(ChatResponseMessage? Message);

internal sealed record ChatResponseMessage(string? Content);

internal sealed record ChatUsage(int? PromptTokens, int? CompletionTokens, int? TotalTokens);

internal sealed record ChatError(JsonElement? Code, string? Message);

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower)]
[JsonSerializable(typeof(ChatRequest))]
[JsonSerializable(typeof(ChatResponse))]
internal sealed partial class OpenRouterJsonContext : JsonSerializerContext;

/// <summary>Sets the key on every request; kept out of the request code so it never ends up in a log line.</summary>
internal static class OpenRouterHttp
{
    public static void Configure(HttpClient client, OpenRouterOptions openRouter, TimeSpan timeout, string siteUrl)
    {
        client.BaseAddress = openRouter.BaseUrl;
        client.Timeout = timeout;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", openRouter.ApiKey);
        // OpenRouter's app attribution headers.
        client.DefaultRequestHeaders.Add("HTTP-Referer", siteUrl);
        client.DefaultRequestHeaders.Add("X-Title", "beeps");
    }
}
