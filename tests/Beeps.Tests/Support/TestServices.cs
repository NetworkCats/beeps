using Beeps.Features.Beeps;
using Beeps.Features.Live;
using Beeps.Features.Moderation;
using Beeps.Features.Shared;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Beeps.Tests.Support;

/// <summary>Real Services over mocked stores, for the tests of Services that build on them.</summary>
internal static class TestServices
{
    /// <summary>A beep service whose rate limits always allow; its live events go to <paramref name="liveEvents"/>.</summary>
    public static BeepService Beeps(IBeepStore store, IBeepCounterStore counters, ILiveEventStore liveEvents)
    {
        var media = new MediaServiceParts();
        var views = new BeepViews(store, counters, TestData.Avatars, TestData.Media, NullLogger<BeepViews>.Instance);
        return new BeepService(
            store,
            counters,
            media.Store,
            media.Create(),
            views,
            new ImmediateUnitOfWork(),
            Substitute.For<IOutbox>(),
            Substitute.For<IModerationJobs>(),
            TestData.RateLimits(media.RateLimits),
            TestData.Live(liveEvents, counters),
            TestData.Inboxes(liveEvents),
            Options.Create(new ModerationOptions()),
            TestData.Time(),
            new TestLocalizer(),
            NullLogger<BeepService>.Instance);
    }
}
