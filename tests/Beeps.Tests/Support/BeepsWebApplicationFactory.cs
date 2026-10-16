using Beeps.Features.RateLimiting;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.DependencyInjection;

namespace Beeps.Tests.Support;

/// <summary>
/// Hosts the real app with every external dependency (PostgreSQL, Valkey) replaced, so tests never
/// need running services. Interfaces the app's code depends on are NSubstitute mocks.
/// </summary>
public sealed class BeepsWebApplicationFactory : WebApplicationFactory<Program>
{
    public BeepsWebApplicationFactory()
    {
        RateLimitStore.TryAcquireAsync(default!, CancellationToken.None).ReturnsForAnyArgs(RateLimitDecision.Allowed);
    }

    public IRateLimitStore RateLimitStore { get; } = Substitute.For<IRateLimitStore>();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        // Never contacted: nothing in these tests reaches the database or Valkey.
        builder.UseSetting("ConnectionStrings:Postgres", "Host=postgres.invalid;Database=beeps");
        builder.UseSetting("ConnectionStrings:Redis", "redis.invalid:6379");
        builder.UseSetting("Outbox:Enabled", "false");

        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IRateLimitStore>();
            services.AddSingleton(RateLimitStore);
            services.Configure<KeyManagementOptions>(options => options.XmlRepository = new InMemoryXmlRepository());
            services.RemoveAll<IDistributedCache>();
            services.AddDistributedMemoryCache();
        });
    }
}
