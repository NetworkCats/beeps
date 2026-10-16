using Beeps.Features.Accounts;
using Beeps.Features.Admin;
using Beeps.Features.Agents;
using Beeps.Features.Email;
using Beeps.Features.RateLimiting;
using Beeps.Features.Shared;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.DependencyInjection;

namespace Beeps.Tests.Support;

/// <summary>
/// Hosts the real app with every external dependency (PostgreSQL, Valkey, Turnstile, email) replaced, so
/// tests never need running services. Interfaces the app's code depends on are NSubstitute mocks; the
/// Services, pages, endpoints and authentication are the real ones.
/// </summary>
public sealed class BeepsWebApplicationFactory : WebApplicationFactory<Program>
{
    public BeepsWebApplicationFactory()
    {
        RateLimitStore.TryAcquireAsync(default!, CancellationToken.None).ReturnsForAnyArgs(RateLimitDecision.Allowed);
        Captcha.VerifyAsync(default, default!, CancellationToken.None).ReturnsForAnyArgs(true);
        Accounts.GetRegistrationModeAsync(CancellationToken.None).ReturnsForAnyArgs(RegistrationMode.Open);
        Agents.ListAgentsAsync(default, CancellationToken.None).ReturnsForAnyArgs([]);
    }

    public IRateLimitStore RateLimitStore { get; } = Substitute.For<IRateLimitStore>();

    public IAccountStore Accounts { get; } = Substitute.For<IAccountStore>();

    public IAgentStore Agents { get; } = Substitute.For<IAgentStore>();

    public IAdminStore Admin { get; } = Substitute.For<IAdminStore>();

    public ICaptchaVerifier Captcha { get; } = Substitute.For<ICaptchaVerifier>();

    public IEmailSender Email { get; } = Substitute.For<IEmailSender>();

    public IOutbox Outbox { get; } = Substitute.For<IOutbox>();

    internal ImmediateUnitOfWork UnitOfWork { get; } = new();

    internal InMemorySessionStore Sessions { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        // Never contacted: nothing in these tests reaches the database or Valkey.
        builder.UseSetting("ConnectionStrings:Postgres", "Host=postgres.invalid;Database=beeps");
        builder.UseSetting("ConnectionStrings:Redis", "redis.invalid:6379");
        builder.UseSetting("Outbox:Enabled", "false");
        builder.UseSetting("ScheduledJobs:Enabled", "false");
        builder.UseSetting("Site:BaseUrl", "https://beeps.test");
        builder.UseSetting("Turnstile:SiteKey", "test-site-key");
        builder.UseSetting("Turnstile:SecretKey", "test-secret-key");
        builder.UseSetting("Email:Provider", "Smtp");
        builder.UseSetting("Email:Smtp:Host", "smtp.invalid");

        builder.ConfigureTestServices(services =>
        {
            Replace(services, RateLimitStore);
            Replace(services, Accounts);
            Replace(services, Agents);
            Replace(services, Admin);
            Replace(services, Captcha);
            Replace(services, Email);
            Replace(services, Outbox);
            Replace<IUnitOfWork>(services, UnitOfWork);
            Replace<ISessionStore>(services, Sessions);
            services.Configure<KeyManagementOptions>(options => options.XmlRepository = new InMemoryXmlRepository());
            services.RemoveAll<IDistributedCache>();
            services.AddDistributedMemoryCache();
        });
    }

    private static void Replace<T>(IServiceCollection services, T instance)
        where T : class
    {
        services.RemoveAll<T>();
        services.AddSingleton(instance);
    }
}
