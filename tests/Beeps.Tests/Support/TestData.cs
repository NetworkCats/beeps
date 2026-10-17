using Beeps.Features.Accounts;
using Beeps.Features.Avatars;
using Beeps.Features.Beeps;
using Beeps.Features.Live;
using Beeps.Features.Media;
using Beeps.Features.RateLimiting;
using Beeps.Features.Shared;
using Beeps.Features.Site;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Beeps.Tests.Support;

internal static class TestData
{
    public static readonly DateTimeOffset Now = new(2026, 10, 16, 8, 30, 0, TimeSpan.Zero);

    public static readonly RequestOrigin Origin = new("203.0.113.7", "en");

    public static IOptions<SiteOptions> Site { get; } = Options.Create(new SiteOptions { BaseUrl = "https://beeps.test" });

    public static IOptions<MediaOptions> MediaOptions { get; } = Options.Create(new MediaOptions { PublicBaseUrl = "https://media.beeps.test" });

    public static MediaUrls Media { get; } = new(MediaOptions);

    public static AvatarUrls Avatars { get; } = new(Site, Media);

    public static TimeProvider Time()
    {
        var time = Substitute.For<TimeProvider>();
        time.GetUtcNow().Returns(Now);
        return time;
    }

    /// <summary>The real live-event publisher over a mocked store, so tests can see which events a change published.</summary>
    public static LiveUpdates Live(ILiveEventStore store, IBeepCounterStore? counters = null) =>
        new(store, counters ?? Substitute.For<IBeepCounterStore>(), NullLogger<LiveUpdates>.Instance);

    /// <summary>The live events of one type a mocked store received, with their JSON data.</summary>
    public static List<string> LiveEvents(ILiveEventStore store, string type) =>
        [.. store.ReceivedCalls()
            .Where(c => c.GetMethodInfo().Name == nameof(ILiveEventStore.AppendAsync) && (string)c.GetArguments()[0]! == type)
            .Select(c => (string)c.GetArguments()[1]!)];

    /// <summary>A real rate-limit service over a mocked store: every policy configured, the store decides.</summary>
    public static RateLimitService RateLimits(IRateLimitStore store)
    {
        var limits = Substitute.For<IOptionsMonitor<RateLimitOptions>>();
        limits.CurrentValue.Returns(new RateLimitOptions
        {
            Policies = RateLimitPolicies.All.ToDictionary(p => p, _ => new RateLimitPolicyOptions { Rules = [new RateLimitRuleOptions { PermitLimit = 1, Window = TimeSpan.FromMinutes(1) }] }),
        });
        return new RateLimitService(store, new LocalRateLimiter(TimeProvider.System), limits, new TestLocalizer(), NullLogger<RateLimitService>.Instance);
    }

    /// <summary>A real rate-limit service that allows everything, for Services whose limits a test does not look at.</summary>
    public static RateLimitService AllowingRateLimits()
    {
        var store = Substitute.For<IRateLimitStore>();
        store.TryAcquireAsync(default!, CancellationToken.None).ReturnsForAnyArgs(RateLimitDecision.Allowed);
        return RateLimits(store);
    }

    /// <summary>The policies a rate-limit check counted, from the counter keys (<c>{policy}:{window}:{partition}</c>).</summary>
    public static string[] Policies(IReadOnlyList<RateLimitCounter> counters) =>
        [.. counters.Select(c => c.Key[..c.Key.IndexOf(':', StringComparison.Ordinal)])];

    public static User User(string handle = "tom", bool admin = false, bool verified = true, string? passwordHash = "hash")
    {
        var id = Guid.CreateVersion7(Now);
        return new User
        {
            Id = id,
            Actor = new Actor
            {
                Id = id,
                Kind = ActorKind.Human,
                Handle = handle,
                NormalizedHandle = handle.ToLowerInvariant(),
                DisplayName = handle,
                CreatedAt = Now,
            },
            Email = $"{handle}@example.com",
            NormalizedEmail = $"{handle.ToLowerInvariant()}@example.com",
            PasswordHash = passwordHash ?? "",
            EmailVerifiedAt = verified ? Now : null,
            IsAdmin = admin,
            Locale = "en",
            CreatedAt = Now,
        };
    }
}
