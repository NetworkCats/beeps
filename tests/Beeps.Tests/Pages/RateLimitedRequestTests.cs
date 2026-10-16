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
    public async Task DeniedRequestsGet429WithRetryAfter()
    {
        factory.RateLimitStore.TryAcquireAsync(default!, TestSupport.Ct).ReturnsForAnyArgs(RateLimitDecision.Denied(TimeSpan.FromSeconds(90)));
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Accept.ParseAdd("application/json");

        using var response = await client.GetAsync(new Uri("/", UriKind.Relative), TestSupport.Ct);

        Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);
        Assert.Equal(TimeSpan.FromSeconds(90), response.Headers.RetryAfter?.Delta);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestSupport.Ct));
        Assert.Equal("rate_limited", json.RootElement.GetProperty("code").GetString());
    }

    public void Dispose() => factory.Dispose();
}
