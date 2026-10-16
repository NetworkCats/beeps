using System.Text.Json;
using Beeps.Features.Accounts;
using Beeps.Features.Avatars;
using Beeps.Features.Beeps;
using Beeps.Features.Notifications;
using Beeps.Features.Shared;
using Beeps.Tests.Support;
using Microsoft.Extensions.Logging.Abstractions;

namespace Beeps.Tests.Features.Notifications;

public sealed class NotificationTests
{
    private static readonly Guid Author = Guid.NewGuid();
    private static readonly Guid Parent = Guid.NewGuid();
    private static readonly Guid BeepId = Guid.NewGuid();

    private readonly INotificationStore store = Substitute.For<INotificationStore>();
    private readonly IBeepStore beeps = Substitute.For<IBeepStore>();
    private readonly IBeepCounterStore counters = Substitute.For<IBeepCounterStore>();
    private readonly List<Notification> added = [];

    public NotificationTests()
    {
        store.AddAsync(default!, TestSupport.Ct).ReturnsForAnyArgs(ci =>
        {
            added.AddRange(ci.Arg<IReadOnlyCollection<Notification>>() ?? []);
            return Task.CompletedTask;
        });
        beeps.GetMentionsAsync(default!, TestSupport.Ct).ReturnsForAnyArgs(new Dictionary<Guid, IReadOnlyList<ActorRow>>());
        counters.GetAsync(default!, TestSupport.Ct).ReturnsForAnyArgs(new Dictionary<Guid, BeepCounts>());
    }

    [Fact]
    public async Task ARepliedToAuthorHearsOnceAndAuthorsNeverHearOfThemselves()
    {
        var mentioned = Guid.NewGuid();
        store.GetBeepAudienceAsync(BeepId, Arg.Any<CancellationToken>()).Returns(new BeepAudience(Author, Parent, [Parent, Author, mentioned]));

        await new BeepNotificationsHandler(store, TestData.Time()).HandleAsync(Envelope(new BeepNotificationsRequest(BeepId)), TestSupport.Ct);

        Assert.Equal(
            [(Parent, NotificationType.Reply), (mentioned, NotificationType.Mention)],
            added.Select(n => (n.RecipientId, n.Type)));
        Assert.All(added, n => Assert.Equal((Author, BeepId, TestData.Now), (n.FromActorId, n.BeepId, n.CreatedAt)));
    }

    [Fact]
    public async Task RepliesToYourselfNotifyNobody()
    {
        store.GetBeepAudienceAsync(BeepId, Arg.Any<CancellationToken>()).Returns(new BeepAudience(Author, Author, []));

        await new BeepNotificationsHandler(store, TestData.Time()).HandleAsync(Envelope(new BeepNotificationsRequest(BeepId)), TestSupport.Ct);
        await new BeepNotificationsHandler(store, TestData.Time()).HandleAsync(Envelope(new BeepNotificationsRequest(Guid.NewGuid())), TestSupport.Ct);

        await store.DidNotReceiveWithAnyArgs().AddAsync(default!, TestSupport.Ct);
    }

    [Fact]
    public async Task ALikeNotifiesTheAuthorUnlessTakenBackOrTheirOwn()
    {
        var liker = Guid.NewGuid();
        var handler = new LikeNotificationHandler(store, TestData.Time());
        store.GetLikedBeepAuthorAsync(BeepId, liker, Arg.Any<CancellationToken>()).Returns(Author);
        store.GetLikedBeepAuthorAsync(BeepId, Author, Arg.Any<CancellationToken>()).Returns(Author);

        await handler.HandleAsync(Envelope(new LikeNotificationRequest(BeepId, liker)), TestSupport.Ct);
        await handler.HandleAsync(Envelope(new LikeNotificationRequest(BeepId, Author)), TestSupport.Ct);
        await handler.HandleAsync(Envelope(new LikeNotificationRequest(BeepId, Guid.NewGuid())), TestSupport.Ct);

        var like = Assert.Single(added);
        Assert.Equal((Author, NotificationType.Like, liker), (like.RecipientId, like.Type, like.FromActorId));
    }

    [Fact]
    public async Task TheInboxShowsTheBeepsTheRecipientCanStillSee()
    {
        var agent = Viewer.Agent(Guid.NewGuid(), Author);
        var from = new ActorRow(Author, ActorKind.Human, "tom", "Tom", null);
        var gone = Guid.NewGuid();
        var page = PageRequest.Create(limit: 5);
        store.ListAsync(agent.ActorId, page, true, Arg.Any<CancellationToken>()).Returns(new CursorPage<NotificationRow>(
            [
                new NotificationRow(Guid.NewGuid(), NotificationType.Mention, from, BeepId, TestData.Now, null),
                new NotificationRow(Guid.NewGuid(), NotificationType.Like, from, gone, TestData.Now, TestData.Now),
            ],
            Guid.NewGuid()));
        beeps.GetRowsAsync(Arg.Any<IReadOnlyCollection<Guid>>(), agent.ActorId, Arg.Any<CancellationToken>())
            .Returns([new BeepRow(BeepId, "hey @tom.claude", from, null, BeepId, TestData.Now, 0, 0, false)]);

        var result = await CreateService().ListAsync(agent, page, unreadOnly: true, TestSupport.Ct);

        var item = Assert.Single(result.Items);
        Assert.Equal(Relations.Owner, item.From.Relation);
        Assert.Equal(BeepId, item.Beep.Id);
        Assert.False(item.Read);
        Assert.NotNull(result.NextCursor);
    }

    [Fact]
    public async Task MarkingReadIsForTheViewersOwnInbox()
    {
        var agent = Viewer.Agent(Guid.NewGuid(), Author);
        var upTo = Guid.NewGuid();
        store.MarkReadAsync(agent.ActorId, upTo, TestData.Now, Arg.Any<CancellationToken>()).Returns(3);

        Assert.Equal(3, await CreateService().MarkReadAsync(agent, upTo, TestSupport.Ct));
    }

    private static OutboxEnvelope Envelope(BeepNotificationsRequest payload) =>
        new(Guid.NewGuid(), BeepNotificationsRequest.MessageType, JsonSerializer.Serialize(payload, NotificationsJsonContext.Default.BeepNotificationsRequest), 1, TestData.Now);

    private static OutboxEnvelope Envelope(LikeNotificationRequest payload) =>
        new(Guid.NewGuid(), LikeNotificationRequest.MessageType, JsonSerializer.Serialize(payload, NotificationsJsonContext.Default.LikeNotificationRequest), 1, TestData.Now);

    private NotificationService CreateService() =>
        new(store, beeps, new BeepViews(beeps, counters, new AvatarUrls(TestData.Site), NullLogger<BeepViews>.Instance), TestData.Time());
}
