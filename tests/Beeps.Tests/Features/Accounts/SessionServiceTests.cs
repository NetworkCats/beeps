using System.Globalization;
using Beeps.Features.Accounts;
using Beeps.Features.Live;
using Beeps.Features.Shared;
using Beeps.Tests.Support;

namespace Beeps.Tests.Features.Accounts;

public sealed class SessionServiceTests
{
    private static readonly Guid UserId = Guid.CreateVersion7(TestData.Now);
    private static readonly AccountState Account = new(UserId, "tom", "Tom", false, true, "en");

    private readonly ISessionStore sessions = Substitute.For<ISessionStore>();
    private readonly IAccountStore accounts = Substitute.For<IAccountStore>();
    private readonly ImmediateUnitOfWork unitOfWork = new();
    private readonly ILiveEventStore liveEvents = Substitute.For<ILiveEventStore>();
    private readonly IIpLocations locations = Substitute.For<IIpLocations>();

    public SessionServiceTests() =>
        accounts.GetAccountStateAsync(UserId, Arg.Any<CancellationToken>()).Returns(Account);

    [Fact]
    public async Task StartingStoresOnlyTheHashOfTheToken()
    {
        UserSession? stored = null;
        sessions.When(s => s.Add(Arg.Any<UserSession>())).Do(ci => stored = ci.Arg<UserSession>());

        var token = await CreateService().StartAsync(UserId, new SessionClient(new string('x', 300), "203.0.113.7"), TestSupport.Ct);

        Assert.True(SessionTokens.IsWellFormed(token));
        Assert.Equal(SessionTokens.Hash(token), stored?.TokenHash);
        Assert.DoesNotContain(token, stored!.TokenHash, StringComparison.Ordinal);
        Assert.Equal(UserId, stored.UserId);
        Assert.Equal(TestData.Now + SessionService.Lifetime, stored.ExpiresAt);
        Assert.Equal(UserSession.UserAgentMaxLength, stored.UserAgent?.Length);
        Assert.Equal("203.0.113.7", stored.IpAddress);
        Assert.Equal(1, unitOfWork.Saves);
    }

    [Fact]
    public async Task StartingTellsTheAccountsDevicesPages()
    {
        await CreateService().StartAsync(UserId, new SessionClient("curl/8", null), TestSupport.Ct);

        Assert.Equal([$$"""{"account_id":"{{UserId:D}}"}"""], TestData.LiveEvents(liveEvents, LiveEventTypes.SessionStarted));
    }

    [Fact]
    public async Task DevicesAreNamedAndPlacedThisOneFirst()
    {
        var current = Guid.CreateVersion7(TestData.Now);
        var other = Guid.CreateVersion7(TestData.Now.AddMinutes(-1));
        const string iPhone = "Mozilla/5.0 (iPhone; CPU iPhone OS 18_0 like Mac OS X) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/18.0 Mobile/15E148 Safari/604.1";
        sessions.ListActiveAsync(UserId, current, TestData.Now, SessionService.DeviceListLimit, Arg.Any<CancellationToken>()).Returns(
        [
            new ActiveSession(current, TestData.Now.AddDays(-3), TestData.Now, iPhone, "203.0.113.7"),
            new ActiveSession(other, TestData.Now.AddDays(-9), TestData.Now.AddHours(-2), null, "198.51.100.1"),
        ]);
        locations.Find("203.0.113.7", Arg.Any<CultureInfo>()).Returns(new IpLocation("Shanghai", "Shanghai", "China"));

        var devices = await CreateService().ListDevicesAsync(UserId, current, TestSupport.Ct);

        Assert.Equal(
            [
                new SignedInDevice(current, new DeviceInfo(DeviceKind.Phone, "iPhone", "Safari"), new DevicePlace("Shanghai", "China"), TestData.Now.AddDays(-3), TestData.Now, IsCurrent: true),
                new SignedInDevice(other, DeviceInfo.Unknown, null, TestData.Now.AddDays(-9), TestData.Now.AddHours(-2), IsCurrent: false),
            ],
            devices);
    }

    [Theory]
    [InlineData(null, "Bavaria", "Germany", "Bavaria")]
    [InlineData("Singapore", "Singapore", "Singapore", null)]
    [InlineData(null, null, "United States", null)]
    public async Task APlaceIsItsTownOrRegionAndCountry(string? city, string? region, string country, string? locality)
    {
        var id = Guid.CreateVersion7(TestData.Now);
        sessions.ListActiveAsync(UserId, id, TestData.Now, SessionService.DeviceListLimit, Arg.Any<CancellationToken>())
            .Returns([new ActiveSession(id, TestData.Now, TestData.Now, null, "192.0.2.1")]);
        locations.Find("192.0.2.1", Arg.Any<CultureInfo>()).Returns(new IpLocation(city, region, country));

        var devices = await CreateService().ListDevicesAsync(UserId, id, TestSupport.Ct);

        Assert.Equal(new DevicePlace(locality, country), Assert.Single(devices).Place);
    }

    [Fact]
    public async Task RemovingADeviceLogsItsTabsOut()
    {
        var other = Guid.NewGuid();
        sessions.RevokeByIdAsync(UserId, other, TestData.Now, Arg.Any<CancellationToken>()).Returns(true);

        var result = await CreateService().EndDeviceAsync(UserId, Guid.NewGuid(), other, TestSupport.Ct);

        Assert.True(result.IsSuccess);
        Assert.Equal([$$"""{"account_id":"{{UserId:D}}"}"""], TestData.LiveEvents(liveEvents, LiveEventTypes.SessionsEnded));
    }

    [Fact]
    public async Task RemovingADeviceAlreadyGoneIsDone()
    {
        var result = await CreateService().EndDeviceAsync(UserId, Guid.NewGuid(), Guid.NewGuid(), TestSupport.Ct);

        Assert.True(result.IsSuccess);
        Assert.Empty(TestData.LiveEvents(liveEvents, LiveEventTypes.SessionsEnded));
    }

    [Fact]
    public async Task ThisDeviceIsNotRemovedButLoggedOut()
    {
        var current = Guid.NewGuid();

        var result = await CreateService().EndDeviceAsync(UserId, current, current, TestSupport.Ct);

        Assert.Equal(ErrorKind.Validation, result.Error?.Kind);
        Assert.Equal("Sessions.Error.Current", result.Error?.Detail);
        await sessions.DidNotReceiveWithAnyArgs().RevokeByIdAsync(default, default, default, TestSupport.Ct);
    }

    [Fact]
    public async Task LoggingOutOtherDevicesKeepsThisOne()
    {
        var current = Guid.NewGuid();

        var result = await CreateService().EndOtherDevicesAsync(UserId, current, TestSupport.Ct);

        Assert.True(result.IsSuccess);
        await sessions.Received(1).RevokeOthersAsync(UserId, current, TestData.Now, Arg.Any<CancellationToken>());
        // Saved with the durable removal of the cached sessions it stages, then removed at once.
        Assert.Equal(1, unitOfWork.Saves);
        await sessions.Received(1).ForgetAllAsync(UserId);
        Assert.Equal([$$"""{"account_id":"{{UserId:D}}"}"""], TestData.LiveEvents(liveEvents, LiveEventTypes.SessionsEnded));
    }

    [Fact]
    public async Task LoggingOutOtherDevicesNeedsThisOne()
    {
        var result = await CreateService().EndOtherDevicesAsync(UserId, null, TestSupport.Ct);

        Assert.False(result.IsSuccess);
        await sessions.DidNotReceiveWithAnyArgs().RevokeOthersAsync(default, default, default, TestSupport.Ct);
    }

    [Fact]
    public async Task ALiveSessionResolvesToItsAccount()
    {
        var token = Arrange(lastSeen: TestData.Now.AddMinutes(-5), expires: TestData.Now.AddDays(10));

        var session = await CreateService().ResolveAsync(token, TestSupport.Ct);

        Assert.Same(Account, session?.Account);
        Assert.False(session?.Renewed);
        await sessions.DidNotReceiveWithAnyArgs().TouchAsync(default!, default, default, TestSupport.Ct);
    }

    [Fact]
    public async Task UseAfterAWhileSlidesTheExpiry()
    {
        var token = Arrange(lastSeen: TestData.Now - SessionService.TouchResolution, expires: TestData.Now.AddDays(1));

        var session = await CreateService().ResolveAsync(token, TestSupport.Ct);

        Assert.True(session?.Renewed);
        Assert.Equal(TestData.Now + SessionService.Lifetime, session?.ExpiresAt);
        await sessions.Received(1).TouchAsync(Arg.Any<SessionRow>(), TestData.Now, TestData.Now + SessionService.Lifetime, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExpiredSessionsAreRejected()
    {
        var token = Arrange(lastSeen: TestData.Now.AddDays(-31), expires: TestData.Now);

        Assert.Null(await CreateService().ResolveAsync(token, TestSupport.Ct));
    }

    [Fact]
    public async Task UnknownOrRevokedTokensAreRejected()
    {
        Assert.Null(await CreateService().ResolveAsync(SessionTokens.Generate(), TestSupport.Ct));
    }

    [Fact]
    public async Task ABannedOrDeletedAccountEndsTheSession()
    {
        var token = Arrange(lastSeen: TestData.Now, expires: TestData.Now.AddDays(10));
        accounts.GetAccountStateAsync(UserId, Arg.Any<CancellationToken>()).Returns((AccountState?)null);

        Assert.Null(await CreateService().ResolveAsync(token, TestSupport.Ct));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not a token")]
    public async Task MalformedTokensNeverReachTheStore(string? token)
    {
        Assert.Null(await CreateService().ResolveAsync(token, TestSupport.Ct));
        await CreateService().EndAsync(token, TestSupport.Ct);

        await sessions.DidNotReceiveWithAnyArgs().FindAsync(default!, TestSupport.Ct);
        await sessions.DidNotReceiveWithAnyArgs().RevokeAsync(default!, default, TestSupport.Ct);
    }

    [Fact]
    public async Task EndingRevokesTheSession()
    {
        var token = SessionTokens.Generate();

        await CreateService().EndAsync(token, TestSupport.Ct);

        await sessions.Received(1).RevokeAsync(SessionTokens.Hash(token), TestData.Now, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task EndingTellsTheAccountsOpenTabs()
    {
        var token = Arrange(TestData.Now, TestData.Now.AddDays(1));

        await CreateService().EndAsync(token, TestSupport.Ct);

        // Its other tabs check whether theirs was the one that ended.
        Assert.Equal([$$"""{"account_id":"{{UserId:D}}"}"""], TestData.LiveEvents(liveEvents, LiveEventTypes.SessionsEnded));
    }

    private string Arrange(DateTimeOffset lastSeen, DateTimeOffset expires)
    {
        var token = SessionTokens.Generate();
        var hash = SessionTokens.Hash(token);
        sessions.FindAsync(hash, Arg.Any<CancellationToken>()).Returns(new SessionRow(Guid.NewGuid(), UserId, hash, lastSeen, expires));
        return token;
    }

    private SessionService CreateService() =>
        new(sessions, accounts, unitOfWork, locations, TestData.Live(liveEvents), new TestLocalizer(), TestData.Time());
}
