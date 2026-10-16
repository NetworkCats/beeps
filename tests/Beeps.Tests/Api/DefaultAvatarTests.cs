using System.Net;
using Beeps.Tests.Support;

namespace Beeps.Tests.Api;

public sealed class DefaultAvatarTests(BeepsWebApplicationFactory factory) : IClassFixture<BeepsWebApplicationFactory>
{
    [Fact]
    public async Task IsAStableCacheableSvgPerActor()
    {
        using var client = factory.CreateClient();
        var id = Guid.NewGuid();

        using var first = await client.GetAsync(new Uri($"/avatars/{id}.svg", UriKind.Relative), TestSupport.Ct);
        var again = await client.GetStringAsync(new Uri($"/avatars/{id}.svg", UriKind.Relative), TestSupport.Ct);
        var other = await client.GetStringAsync(new Uri($"/avatars/{Guid.NewGuid()}.svg", UriKind.Relative), TestSupport.Ct);
        var body = await first.Content.ReadAsStringAsync(TestSupport.Ct);

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal("image/svg+xml", first.Content.Headers.ContentType?.MediaType);
        Assert.StartsWith("<svg", body, StringComparison.Ordinal);
        Assert.Equal(body, again);
        Assert.NotEqual(body, other);
        Assert.Contains("immutable", first.Headers.CacheControl?.ToString(), StringComparison.Ordinal);
        Assert.Equal("nosniff", first.Headers.GetValues("X-Content-Type-Options").Single());
    }

    [Fact]
    public async Task OnlyActorIdsAreAccepted()
    {
        using var client = factory.CreateClient();

        using var response = await client.GetAsync(new Uri("/avatars/tom@example.com.svg", UriKind.Relative), TestSupport.Ct);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
