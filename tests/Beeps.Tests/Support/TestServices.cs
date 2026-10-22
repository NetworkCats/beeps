using Beeps.Features.Beeps;
using Beeps.Features.Live;
using Beeps.Features.Moderation;
using Beeps.Features.Profiles;
using Beeps.Features.RateLimiting;
using Beeps.Features.Shared;
using Beeps.Features.Translation;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Beeps.Tests.Support;

/// <summary>Real Services over mocked stores, for the tests of Services that build on them.</summary>
internal static class TestServices
{
    public static BeepViews Views(IBeepStore store, IBeepCounterStore counters) =>
        new(store, counters, TestData.Avatars, TestData.Media, NullLogger<BeepViews>.Instance);

    /// <summary>A beep service whose rate limits always allow; its live events go to <paramref name="liveEvents"/>.</summary>
    public static BeepService Beeps(IBeepStore store, IBeepCounterStore counters, ILiveEventStore liveEvents)
    {
        var media = new MediaServiceParts();
        return new BeepService(
            store,
            counters,
            media.Store,
            media.Create(),
            Views(store, counters),
            new ImmediateUnitOfWork(),
            Substitute.For<IOutbox>(),
            Substitute.For<IModerationJobs>(),
            Substitute.For<ITranslationJobs>(),
            TestData.RateLimits(media.RateLimits),
            TestData.Live(liveEvents, counters),
            TestData.Inboxes(liveEvents),
            Options.Create(new ModerationOptions()),
            TestData.Time(),
            new TestLocalizer(),
            NullLogger<BeepService>.Instance);
    }

    /// <summary>A profile service whose rate limits always allow, unless a test passes its own.</summary>
    public static ProfileService Profiles(
        IProfileStore store,
        BeepViews views,
        IUnitOfWork? unitOfWork = null,
        RateLimitService? rateLimits = null,
        ILiveEventStore? liveEvents = null) =>
        new(
            store,
            views,
            TestData.Media,
            unitOfWork ?? new ImmediateUnitOfWork(),
            rateLimits ?? TestData.AllowingRateLimits(),
            TestData.Live(liveEvents ?? Substitute.For<ILiveEventStore>()),
            TestData.Time(),
            new TestLocalizer(),
            NullLogger<ProfileService>.Instance);
}
