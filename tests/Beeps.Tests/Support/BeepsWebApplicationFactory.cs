using Beeps.Features.Accounts;
using Beeps.Features.Admin;
using Beeps.Features.Agents;
using Beeps.Features.Avatars;
using Beeps.Features.Beeps;
using Beeps.Features.Email;
using Beeps.Features.Live;
using Beeps.Features.Media;
using Beeps.Features.Moderation;
using Beeps.Features.Notifications;
using Beeps.Features.Profiles;
using Beeps.Features.RateLimiting;
using Beeps.Features.Search;
using Beeps.Features.Shared;
using Beeps.Features.Timelines;
using Beeps.Infrastructure.Caching;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.DependencyInjection;

namespace Beeps.Tests.Support;

/// <summary>
/// Hosts the real app with every external dependency (PostgreSQL, Valkey, Turnstile, email, object storage) replaced, so
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
        Counters.GetAsync(default!, CancellationToken.None).ReturnsForAnyArgs(new Dictionary<Guid, BeepCounts>());
        Beeps.GetMentionsAsync(default!, CancellationToken.None).ReturnsForAnyArgs(new Dictionary<Guid, IReadOnlyList<ActorRow>>());
        Beeps.GetRowsAsync(default!, default, CancellationToken.None).ReturnsForAnyArgs([]);
        Beeps.GetMediaAsync(default!, CancellationToken.None).ReturnsForAnyArgs(new Dictionary<Guid, IReadOnlyList<MediaRow>>());
        // Pages read timelines; empty unless a test arranges beeps.
        Timelines.PublicAsync(default!, default, CancellationToken.None).ReturnsForAnyArgs(new CursorPage<BeepRow>([], null));
        Timelines.HomeAsync(default, default!, default, CancellationToken.None).ReturnsForAnyArgs(new CursorPage<BeepRow>([], null));
        Timelines.ProfileAsync(default, default, default!, default, CancellationToken.None).ReturnsForAnyArgs(new CursorPage<BeepRow>([], null));
        Timelines.HashtagAsync(default!, default!, default, CancellationToken.None).ReturnsForAnyArgs(new CursorPage<BeepRow>([], null));
        Notifications.ListAsync(default, default!, default, CancellationToken.None).ReturnsForAnyArgs(new CursorPage<NotificationRow>([], null));
        // Nobody has a banner unless a test arranges one.
        Avatars.FindBannerKeyAsync(default, CancellationToken.None).ReturnsForAnyArgs((string?)null);
    }

    public IRateLimitStore RateLimitStore { get; } = Substitute.For<IRateLimitStore>();

    public IAccountStore Accounts { get; } = Substitute.For<IAccountStore>();

    public IAgentStore Agents { get; } = Substitute.For<IAgentStore>();

    public IAdminStore Admin { get; } = Substitute.For<IAdminStore>();

    public ICaptchaVerifier Captcha { get; } = Substitute.For<ICaptchaVerifier>();

    public IEmailSender Email { get; } = Substitute.For<IEmailSender>();

    public IOutbox Outbox { get; } = Substitute.For<IOutbox>();

    public IBeepStore Beeps { get; } = Substitute.For<IBeepStore>();

    public IBeepCounterStore Counters { get; } = Substitute.For<IBeepCounterStore>();

    public ITimelineStore Timelines { get; } = Substitute.For<ITimelineStore>();

    public IProfileStore Profiles { get; } = Substitute.For<IProfileStore>();

    public INotificationStore Notifications { get; } = Substitute.For<INotificationStore>();

    public IIdempotencyStore Idempotency { get; } = Substitute.For<IIdempotencyStore>();

    /// <summary>Uploads are decoded by the real SkiaSharp processor; only where they are stored is mocked.</summary>
    public IMediaStorage Storage { get; } = Substitute.For<IMediaStorage>();

    public IMediaStore Media { get; } = Substitute.For<IMediaStore>();

    public IAvatarStore Avatars { get; } = Substitute.For<IAvatarStore>();

    public ILiveEventStore LiveEvents { get; } = Substitute.For<ILiveEventStore>();

    /// <summary>Hands each test's events to the open streams; see <see cref="TestLiveFeed"/>.</summary>
    public TestLiveFeed LiveFeed { get; } = new();

    public ISearchIndex SearchIndex { get; } = Substitute.For<ISearchIndex>();

    public ISearchStore SearchStore { get; } = Substitute.For<ISearchStore>();

    public IModerationStore Moderation { get; } = Substitute.For<IModerationStore>();

    public IModerationJobs ModerationJobs { get; } = Substitute.For<IModerationJobs>();

    public IContentModerator Moderator { get; } = Substitute.For<IContentModerator>();

    public ISystemStore System { get; } = Substitute.For<ISystemStore>();

    /// <summary>The content cache's Valkey side; the real stores that use it are mocked, so it mostly stays empty.</summary>
    internal InMemorySharedContentCache SharedCache { get; } = new();

    internal ImmediateUnitOfWork UnitOfWork { get; } = new();

    internal InMemorySessionStore Sessions { get; } = new();

    /// <summary>A clock for tests whose pages must come out the same every time (the footer's year, relative times).</summary>
    public TimeProvider? Time { get; init; }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        // As in Development: a singleton holding a scoped service fails here, not first in a real run.
        builder.UseDefaultServiceProvider(o =>
        {
            o.ValidateScopes = true;
            o.ValidateOnBuild = true;
        });
        // Never contacted: nothing in these tests reaches the database or Valkey.
        builder.UseSetting("ConnectionStrings:Postgres", "Host=postgres.invalid;Database=beeps");
        builder.UseSetting("ConnectionStrings:Redis", "redis.invalid:6379");
        builder.UseSetting("Outbox:Enabled", "false");
        builder.UseSetting("ScheduledJobs:Enabled", "false");
        builder.UseSetting("Moderation:WorkerEnabled", "false");
        builder.UseSetting("OpenRouter:ApiKey", "test");
        builder.UseSetting("Live:Enabled", "false");
        builder.UseSetting("Search:Url", "http://meilisearch.invalid");
        builder.UseSetting("Search:ApiKey", "test");
        builder.UseSetting("Site:BaseUrl", "https://beeps.test");
        builder.UseSetting("Turnstile:SiteKey", "test-site-key");
        builder.UseSetting("Turnstile:SecretKey", "test-secret-key");
        builder.UseSetting("Email:Provider", "Smtp");
        builder.UseSetting("Email:Smtp:Host", "smtp.invalid");
        builder.UseSetting("Media:PublicBaseUrl", "https://media.beeps.test");
        builder.UseSetting("Storage:ServiceUrl", "https://s3.invalid");
        builder.UseSetting("Storage:Bucket", "beeps-media");
        builder.UseSetting("Storage:AccessKeyId", "test");
        builder.UseSetting("Storage:SecretAccessKey", "test");
        // Tests sharing a factory change mocks between requests; a cached page would hide that. OutputCacheTests turn it on.
        builder.UseSetting("PublicPageCache:Enabled", "false");

        builder.ConfigureTestServices(services =>
        {
            Replace(services, RateLimitStore);
            Replace(services, Accounts);
            Replace(services, Agents);
            Replace(services, Admin);
            Replace(services, Captcha);
            Replace(services, Email);
            Replace(services, Outbox);
            Replace(services, Beeps);
            Replace(services, Counters);
            Replace(services, Timelines);
            Replace(services, Profiles);
            Replace(services, Notifications);
            Replace(services, Idempotency);
            Replace(services, Storage);
            Replace(services, Media);
            Replace(services, Avatars);
            Replace(services, LiveEvents);
            Replace<ILiveEventFeed>(services, LiveFeed);
            Replace(services, SearchIndex);
            Replace(services, SearchStore);
            Replace(services, Moderation);
            Replace(services, ModerationJobs);
            Replace(services, Moderator);
            Replace(services, System);
            Replace<IUnitOfWork>(services, UnitOfWork);
            Replace<ISessionStore>(services, Sessions);
            Replace<ISharedContentCache>(services, SharedCache);
            if (Time is not null)
            {
                Replace(services, Time);
            }

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
