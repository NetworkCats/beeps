using Beeps.Features.Accounts;
using Beeps.Tests.Support;

namespace Beeps.Tests.Features.Accounts;

public sealed class SessionServiceTests
{
    private static readonly Guid UserId = Guid.CreateVersion7(TestData.Now);
    private static readonly AccountState Account = new(UserId, "tom", "Tom", false, true, "en");

    private readonly ISessionStore sessions = Substitute.For<ISessionStore>();
    private readonly IAccountStore accounts = Substitute.For<IAccountStore>();
    private readonly ImmediateUnitOfWork unitOfWork = new();

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

    private string Arrange(DateTimeOffset lastSeen, DateTimeOffset expires)
    {
        var token = SessionTokens.Generate();
        var hash = SessionTokens.Hash(token);
        sessions.FindAsync(hash, Arg.Any<CancellationToken>()).Returns(new SessionRow(Guid.NewGuid(), UserId, hash, lastSeen, expires));
        return token;
    }

    private SessionService CreateService() => new(sessions, accounts, unitOfWork, TestData.Time());
}
