using Beeps.Features.Accounts;
using Beeps.Features.Admin;
using Beeps.Features.Agents;
using Beeps.Features.Avatars;
using Beeps.Features.Shared;
using Beeps.Tests.Support;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Beeps.Tests.Features.Admin;

public sealed class AdminServiceTests
{
    private static readonly Guid AdminId = Guid.CreateVersion7(TestData.Now.AddDays(-1));

    private readonly IAdminStore store = Substitute.For<IAdminStore>();
    private readonly IAccountStore accounts = Substitute.For<IAccountStore>();
    private readonly ISessionStore sessions = Substitute.For<ISessionStore>();
    private readonly IAgentStore agents = Substitute.For<IAgentStore>();
    private readonly ImmediateUnitOfWork unitOfWork = new();
    private readonly List<AdminAuditLog> audit = [];

    public AdminServiceTests()
    {
        store.IsActiveAdminAsync(AdminId, Arg.Any<CancellationToken>()).Returns(true);
        store.CountActiveAdminsAsync(Arg.Any<CancellationToken>()).Returns(2);
        store.When(s => s.AddAuditLog(Arg.Any<AdminAuditLog>())).Do(ci => audit.Add(ci.Arg<AdminAuditLog>()));
        agents.ListAgentsAsync(default, TestSupport.Ct).ReturnsForAnyArgs([]);
    }

    [Fact]
    public async Task EveryMethodChecksTheCallerIsAnAdmin()
    {
        var service = CreateService();
        var outsider = Guid.NewGuid();
        var target = Guid.NewGuid();

        Error?[] errors =
        [
            (await service.ListUsersAsync(outsider, null, null, TestSupport.Ct)).Error,
            (await service.GetUserAsync(outsider, target, TestSupport.Ct)).Error,
            (await service.BanUserAsync(outsider, target, TestSupport.Ct)).Error,
            (await service.UnbanUserAsync(outsider, target, TestSupport.Ct)).Error,
            (await service.DeleteUserAsync(outsider, target, TestSupport.Ct)).Error,
            (await service.SetAdminAsync(outsider, target, true, TestSupport.Ct)).Error,
            (await service.GetRegistrationModeAsync(outsider, TestSupport.Ct)).Error,
            (await service.SetRegistrationModeAsync(outsider, RegistrationMode.Closed, TestSupport.Ct)).Error,
            (await service.ListInvitesAsync(outsider, TestSupport.Ct)).Error,
            (await service.CreateInviteAsync(outsider, TestSupport.Ct)).Error,
            (await service.RevokeInviteAsync(outsider, target, TestSupport.Ct)).Error,
            (await service.ListAuditLogAsync(outsider, null, TestSupport.Ct)).Error,
        ];

        Assert.All(errors, e => Assert.Equal(AdminErrorCodes.NotAdmin, e?.Code));
        Assert.Equal(0, unitOfWork.Saves);
        Assert.Empty(audit);
    }

    [Theory]
    [InlineData(null, null, null)]
    [InlineData("  @Tom ", "tom", null)]
    [InlineData("Tom@Example", null, "tom@example")]
    public async Task SearchIsAHandleOrEmailPrefix(string? query, string? handle, string? email)
    {
        var before = Guid.NewGuid();
        store.ListUsersAsync(default, default, default, default, TestSupport.Ct).ReturnsForAnyArgs(new CursorPage<AdminUserRow>([], null));

        var result = await CreateService().ListUsersAsync(AdminId, query, before, TestSupport.Ct);

        Assert.True(result.IsSuccess);
        await store.Received(1).ListUsersAsync(handle, email, before, AdminService.PageSize, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task UserDetailsIncludeTheirAgents()
    {
        var user = Row(Guid.NewGuid());
        store.GetUserAsync(user.Id, Arg.Any<CancellationToken>()).Returns(user);
        agents.ListAgentsAsync(user.Id, Arg.Any<CancellationToken>()).Returns([new AgentRow(Guid.NewGuid(), null, null, null, TestData.Now, null, null, null, null)]);

        var result = await CreateService().GetUserAsync(AdminId, user.Id, TestSupport.Ct);
        var missing = await CreateService().GetUserAsync(AdminId, Guid.NewGuid(), TestSupport.Ct);

        Assert.Single(result.Value.Agents);
        Assert.Equal(ErrorKind.NotFound, missing.Error?.Kind);
    }

    [Fact]
    public async Task BanningEndsSessionsDisablesAgentsAndIsAudited()
    {
        var user = User();
        var agentIds = new[] { Guid.NewGuid(), Guid.NewGuid() };
        agents.DisableAllAsync(user.Id, TestData.Now, Arg.Any<CancellationToken>()).Returns(agentIds);

        var result = await CreateService().BanUserAsync(AdminId, user.Id, TestSupport.Ct);

        Assert.True(result.IsSuccess);
        Assert.Equal(TestData.Now, user.BannedAt);
        await sessions.Received(1).RevokeAllAsync(user.Id, TestData.Now, Arg.Any<CancellationToken>());
        await sessions.Received(1).ForgetAllAsync(user.Id, Arg.Any<CancellationToken>());
        await accounts.Received(1).ForgetAccountAsync(user.Id, Arg.Any<CancellationToken>());
        Assert.Equal([AdvisoryLocks.Admins], unitOfWork.Locks);
        var entry = Assert.Single(audit);
        Assert.Equal((AuditActions.BanUser, AdminId, user.Id), (entry.Action, entry.AdminId, entry.TargetId));
        await agents.Received(1).ForgetKeysAsync(agentIds, Arg.Any<CancellationToken>());
        Assert.Equal(1, unitOfWork.Saves);
    }

    [Fact]
    public async Task AdminsCannotBanOrDeleteThemselves()
    {
        var service = CreateService();

        var ban = await service.BanUserAsync(AdminId, AdminId, TestSupport.Ct);
        var delete = await service.DeleteUserAsync(AdminId, AdminId, TestSupport.Ct);

        Assert.Equal(AdminErrorCodes.CannotTargetSelf, ban.Error?.Code);
        Assert.Equal(AdminErrorCodes.CannotTargetSelf, delete.Error?.Code);
    }

    [Fact]
    public async Task TheLastAdminCannotBeBannedDeletedOrRevoked()
    {
        var user = User(admin: true);
        store.CountActiveAdminsAsync(Arg.Any<CancellationToken>()).Returns(1);
        var service = CreateService();

        var ban = await service.BanUserAsync(AdminId, user.Id, TestSupport.Ct);
        var delete = await service.DeleteUserAsync(AdminId, user.Id, TestSupport.Ct);
        var revoke = await service.SetAdminAsync(AdminId, user.Id, false, TestSupport.Ct);

        Assert.All([ban.Error, delete.Error, revoke.Error], e => Assert.Equal(AdminErrorCodes.LastAdmin, e?.Code));
        await sessions.DidNotReceiveWithAnyArgs().ForgetAllAsync(default, TestSupport.Ct);
        Assert.True(user.IsAdmin);
        Assert.Null(user.BannedAt);
        Assert.Empty(audit);
        Assert.All(unitOfWork.Locks, l => Assert.Equal(AdvisoryLocks.Admins, l));
    }

    [Fact]
    public async Task AnAdminMayRevokeThemselvesWhenOthersRemain()
    {
        var self = User(admin: true, id: AdminId);

        var result = await CreateService().SetAdminAsync(AdminId, AdminId, false, TestSupport.Ct);

        Assert.True(result.IsSuccess);
        Assert.False(self.IsAdmin);
        Assert.Equal(AuditActions.RevokeAdmin, Assert.Single(audit).Action);
    }

    [Fact]
    public async Task GrantingAdminIsAuditedButNotForBannedUsers()
    {
        var user = User();
        var banned = User();
        banned.BannedAt = TestData.Now;
        var service = CreateService();

        var granted = await service.SetAdminAsync(AdminId, user.Id, true, TestSupport.Ct);
        var refused = await service.SetAdminAsync(AdminId, banned.Id, true, TestSupport.Ct);

        Assert.True(granted.IsSuccess);
        Assert.True(user.IsAdmin);
        Assert.Equal(AdminErrorCodes.UserBanned, refused.Error?.Code);
        Assert.Equal(AuditActions.GrantAdmin, Assert.Single(audit).Action);
        // Only the successful change clears the cached rights.
        await accounts.Received(1).ForgetAccountAsync(user.Id, Arg.Any<CancellationToken>());
        await accounts.DidNotReceive().ForgetAccountAsync(banned.Id, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task UnbanningLeavesTheAgentsDisabled()
    {
        var user = User();
        user.BannedAt = TestData.Now.AddDays(-1);

        var result = await CreateService().UnbanUserAsync(AdminId, user.Id, TestSupport.Ct);

        Assert.True(result.IsSuccess);
        Assert.Null(user.BannedAt);
        Assert.Equal(AuditActions.UnbanUser, Assert.Single(audit).Action);
        await accounts.Received(1).ForgetAccountAsync(user.Id, Arg.Any<CancellationToken>());
        await agents.DidNotReceiveWithAnyArgs().DisableAllAsync(default, default, TestSupport.Ct);
    }

    [Fact]
    public async Task DeletingSoftDeletesTheUserAndTheirAgents()
    {
        var user = User(admin: true);
        agents.DeleteAllAsync(user.Id, TestData.Now, Arg.Any<CancellationToken>()).Returns([Guid.NewGuid()]);

        var result = await CreateService().DeleteUserAsync(AdminId, user.Id, TestSupport.Ct);

        Assert.True(result.IsSuccess);
        Assert.Equal(TestData.Now, user.Actor.DeletedAt);
        Assert.False(user.IsAdmin);
        await sessions.Received(1).RevokeAllAsync(user.Id, TestData.Now, Arg.Any<CancellationToken>());
        await sessions.Received(1).ForgetAllAsync(user.Id, Arg.Any<CancellationToken>());
        Assert.Equal(AuditActions.DeleteUser, Assert.Single(audit).Action);
    }

    [Fact]
    public async Task MissingUsersAreNotFound()
    {
        var result = await CreateService().BanUserAsync(AdminId, Guid.NewGuid(), TestSupport.Ct);

        Assert.Equal(ErrorKind.NotFound, result.Error?.Kind);
    }

    [Fact]
    public async Task ChangingTheRegistrationModeIsAudited()
    {
        accounts.GetRegistrationModeAsync(Arg.Any<CancellationToken>()).Returns(RegistrationMode.Open);
        var service = CreateService();

        var unchanged = await service.SetRegistrationModeAsync(AdminId, RegistrationMode.Open, TestSupport.Ct);
        var changed = await service.SetRegistrationModeAsync(AdminId, RegistrationMode.InviteOnly, TestSupport.Ct);
        var mode = await service.GetRegistrationModeAsync(AdminId, TestSupport.Ct);

        Assert.True(unchanged.IsSuccess && changed.IsSuccess);
        await store.Received(1).SetRegistrationModeAsync(RegistrationMode.InviteOnly, TestData.Now, Arg.Any<CancellationToken>());
        Assert.Equal("Open -> InviteOnly", Assert.Single(audit).Detail);
        Assert.Equal(RegistrationMode.Open, mode.Value);
        Assert.Equal(1, unitOfWork.Saves);
    }

    [Fact]
    public async Task UnknownModesAreRejected()
    {
        var result = await CreateService().SetRegistrationModeAsync(AdminId, (RegistrationMode)42, TestSupport.Ct);

        Assert.Equal(ErrorKind.Validation, result.Error?.Kind);
    }

    [Fact]
    public async Task InvitesAreShownOnceAndStoredAsAHash()
    {
        Invite? stored = null;
        store.When(s => s.AddInvite(Arg.Any<Invite>())).Do(ci => stored = ci.Arg<Invite>());

        var result = await CreateService().CreateInviteAsync(AdminId, TestSupport.Ct);

        Assert.Equal(InviteCodes.Hash(result.Value.Code), stored?.CodeHash);
        Assert.Equal(TestData.Now + AdminService.InviteLifetime, stored?.ExpiresAt);
        Assert.Equal(AdminId, stored?.CreatedBy);
        Assert.Equal(AuditActions.CreateInvite, Assert.Single(audit).Action);
    }

    [Fact]
    public async Task OnlyUnusedInvitesCanBeRevoked()
    {
        var unused = Guid.NewGuid();
        store.DeleteUnusedInviteAsync(unused, Arg.Any<CancellationToken>()).Returns(true);
        var service = CreateService();

        var revoked = await service.RevokeInviteAsync(AdminId, unused, TestSupport.Ct);
        var used = await service.RevokeInviteAsync(AdminId, Guid.NewGuid(), TestSupport.Ct);

        Assert.True(revoked.IsSuccess);
        Assert.Equal(ErrorKind.NotFound, used.Error?.Kind);
        Assert.Equal(AuditActions.RevokeInvite, Assert.Single(audit).Action);
    }

    [Fact]
    public async Task ListsComeFromTheStore()
    {
        var invites = new[] { new InviteRow(Guid.NewGuid(), "AB12", TestData.Now, TestData.Now.AddDays(1), null, null) };
        var log = new CursorPage<AuditLogRow>([], null);
        store.ListInvitesAsync(AdminService.PageSize, Arg.Any<CancellationToken>()).Returns(invites);
        store.ListAuditLogAsync(null, AdminService.PageSize, Arg.Any<CancellationToken>()).Returns(log);
        var service = CreateService();

        Assert.Same(invites, (await service.ListInvitesAsync(AdminId, TestSupport.Ct)).Value);
        Assert.Same(log, (await service.ListAuditLogAsync(AdminId, null, TestSupport.Ct)).Value);
    }

    private User User(bool admin = false, Guid? id = null)
    {
        var user = TestData.User(handle: $"u{Guid.NewGuid():N}"[..10], admin: admin);
        if (id is { } fixedId)
        {
            user = new User
            {
                Id = fixedId,
                Actor = new Actor { Id = fixedId, Kind = ActorKind.Human, Handle = "self", NormalizedHandle = "self", DisplayName = "self" },
                Email = user.Email,
                NormalizedEmail = user.NormalizedEmail,
                PasswordHash = "",
                IsAdmin = admin,
                Locale = "en",
            };
        }

        store.FindUserAsync(user.Id, Arg.Any<CancellationToken>()).Returns(user);
        return user;
    }

    private static AdminUserRow Row(Guid id) => new(id, "tom", "Tom", "tom@example.com", false, true, TestData.Now, null, null);

    private AdminService CreateService()
    {
        var avatars = new AvatarUrls(TestData.Site);
        var localizer = new TestLocalizer();
        var agentService = new AgentService(agents, unitOfWork, avatars, Options.Create(new AgentOptions()), TestData.Time(), localizer, NullLogger<AgentService>.Instance);
        return new AdminService(store, accounts, sessions, agents, agentService, unitOfWork, avatars, TestData.Time(), localizer, NullLogger<AdminService>.Instance);
    }
}
