using System.Net;
using System.Text.Json;
using Beeps.Tests.Support;

namespace Beeps.Tests.Pages;

public sealed class ApiErrorTests(BeepsWebApplicationFactory factory) : IClassFixture<BeepsWebApplicationFactory>
{
    [Fact]
    public async Task UnknownApiRoutesReturnProblemDetailsWithACode()
    {
        using var client = factory.CreateClient();

        using var response = await client.GetAsync(new Uri("/api/v1/no-such-endpoint", UriKind.Relative), TestSupport.Ct);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestSupport.Ct));
        Assert.Equal("not_found", json.RootElement.GetProperty("code").GetString());
        Assert.True(json.RootElement.TryGetProperty("trace_id", out _));
    }
}
