using System.Net;
using Beeps.Tests.Support;

namespace Beeps.Tests.Api;

public sealed class HealthCheckTests(BeepsWebApplicationFactory factory) : IClassFixture<BeepsWebApplicationFactory>
{
    [Fact]
    public async Task IsUnhealthyWithoutTheDatabase()
    {
        using var client = factory.CreateClient();

        // The tests' database (postgres.invalid) is never there: a deploy must not send traffic to such an instance.
        using var response = await client.GetAsync(new Uri("/healthz", UriKind.Relative), TestSupport.Ct);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal("Unhealthy", await response.Content.ReadAsStringAsync(TestSupport.Ct));
    }
}
