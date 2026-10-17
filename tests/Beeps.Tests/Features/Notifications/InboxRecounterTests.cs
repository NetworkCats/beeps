using Beeps.Features.Live;
using Beeps.Features.Notifications;
using Beeps.Tests.Support;

namespace Beeps.Tests.Features.Notifications;

public sealed class InboxRecounterTests
{
    private readonly ILiveEventStore store = Substitute.For<ILiveEventStore>();
    private readonly INotificationStore notifications = Substitute.For<INotificationStore>();

    [Fact]
    public async Task InboxesWithUnreadNotificationsAboutHiddenOrReturningContentAreRecounted()
    {
        var (beep, banned) = (Guid.NewGuid(), Guid.NewGuid());
        var recipients = Enumerable.Range(0, InboxRecounter.BatchSize + 1).Select(_ => Guid.NewGuid()).ToList();
        notifications.ListUnreadRecipientsAsync(
                Arg.Is<IReadOnlyCollection<Guid>>(b => b.SequenceEqual(new[] { beep })),
                Arg.Is<IReadOnlyCollection<Guid>>(a => a.SequenceEqual(new[] { banned })),
                InboxRecounter.MaxRecipients,
                Arg.Any<CancellationToken>())
            .Returns(recipients);
        var inboxes = TestData.Inboxes(store, notifications);

        await inboxes.RecountAsync([beep], [banned], TestSupport.Ct);
        await inboxes.RecountAsync([], [], TestSupport.Ct);

        // In batches, like counts; nothing asked about nothing.
        var events = TestData.LiveEvents(store, LiveEventTypes.Inbox);
        Assert.Equal(2, events.Count);
        Assert.All(recipients, r => Assert.Contains(events, e => e.Contains(r.ToString("D"), StringComparison.Ordinal)));
        await notifications.ReceivedWithAnyArgs(1).ListUnreadRecipientsAsync(default!, default!, default, TestSupport.Ct);
    }

    [Fact]
    public async Task ADatabaseErrorWhileRecountingInboxesNeverFailsTheChange()
    {
        notifications.ListUnreadRecipientsAsync(default!, default!, default, TestSupport.Ct)
            .ReturnsForAnyArgs(Task.FromException<IReadOnlyList<Guid>>(new InvalidOperationException("database is down")));

        await TestData.Inboxes(store, notifications).RecountAsync([Guid.NewGuid()], [], TestSupport.Ct);

        await store.DidNotReceiveWithAnyArgs().AppendAsync(default!, default!, TestSupport.Ct);
    }
}
