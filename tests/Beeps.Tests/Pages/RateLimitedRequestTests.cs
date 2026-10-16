using System.Net;
using System.Text.Json;
using Beeps.Features.RateLimiting;
using Beeps.Tests.Support;

namespace Beeps.Tests.Pages;

/// <summary>Own factory: it switches the shared store mock to "denied".</summary>
public sealed class RateLimitedRequestTests : IDisposable
{
    private readonly BeepsWebApplicationFactory factory = new();

    [Fact]
    public async Task DeniedApiRequestsGetProblemDetailsWithRetryAfter()
    {
        factory.RateLimitStore.TryAcquireAsync(default!, TestSupport.Ct).ReturnsForAnyArgs(RateLimitDecision.Denied(TimeSpan.FromSeconds(90)));
        using var client = factory.CreateClient();

        using var response = await client.GetAsync(new Uri("/api/v1/openapi.json", UriKind.Relative), TestSupport.Ct);

        Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);
        Assert.Equal(TimeSpan.FromSeconds(90), response.Headers.RetryAfter?.Delta);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestSupport.Ct));
        Assert.Equal("rate_limited", json.RootElement.GetProperty("code").GetString());
    }

    [Fact]
    public async Task DeniedPagesGetTheLocalizedErrorPage()
    {
        factory.RateLimitStore.TryAcquireAsync(default!, TestSupport.Ct).ReturnsForAnyArgs(RateLimitDecision.Denied(TimeSpan.FromSeconds(90)));
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.AcceptLanguage.ParseAdd("zh-CN");

        using var response = await client.GetAsync(new Uri("/public", UriKind.Relative), TestSupport.Ct);
        var html = await response.Content.ReadAsStringAsync(TestSupport.Ct);

        Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);
        Assert.Equal(TimeSpan.FromSeconds(90), response.Headers.RetryAfter?.Delta);
        Assert.Equal("text/html", response.Content.Headers.ContentType?.MediaType);
        Assert.Contains("请求太频繁", html, StringComparison.Ordinal);
        // The error page itself is not counted again, or it could never be shown.
        await factory.RateLimitStore.ReceivedWithAnyArgs(1).TryAcquireAsync(default!, TestSupport.Ct);
    }

    public void Dispose() => factory.Dispose();
}
