using System.Net;
using System.Text.RegularExpressions;
using Beeps.Features.Accounts;
using Beeps.Features.Admin;
using Beeps.Features.Agents;
using Beeps.Features.Live;
using Beeps.Features.Shared;
using Beeps.Tests.Support;

namespace Beeps.Tests.Pages;

public sealed partial class AgentAndAdminPagesTests : IDisposable
{
    private readonly BeepsWebApplicationFactory factory = new();

    [Fact]
    public async Task OnboardingShowsTheNewKeyInTheHandOffSentence()
    {
        var user = WebAuth.ArrangeUser(factory);
        using var client = await WebAuth.SignInAsync(factory, user);

        using var response = await WebAuth.PostFormAsync(client, "/onboarding/agent", "/onboarding/agent");
        var html = await response.Content.ReadAsStringAsync(TestSupport.Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var key = KeyPattern().Match(html).Value;
        Assert.True(AgentKeys.IsWellFormed(key));
        Assert.Contains($"Read https://beeps.test/skill.md and follow it to use beeps. Your personal key is: {key}.", html, StringComparison.Ordinal);
        Assert.Contains("data-copy", html, StringComparison.Ordinal);
        Assert.Contains("step step-active", html, StringComparison.Ordinal);
        PageAssert.NoInlineScriptsOrStyles(html);
        factory.Agents.Received(1).AddKey(Arg.Is<AgentKey>(k => k.KeyHash == AgentKeys.Hash(key)));
    }

    [Fact]
    public async Task TheHandOffSentenceFollowsThePageLanguage()
    {
        var user = WebAuth.ArrangeUser(factory, locale: "zh-Hans");
        using var client = await WebAuth.SignInAsync(factory, user);

        using var response = await WebAuth.PostFormAsync(client, "/onboarding/agent", "/onboarding/agent");
        var html = await response.Content.ReadAsStringAsync(TestSupport.Ct);

        Assert.Contains("请阅读 https://beeps.test/skill.md 并按说明使用 beeps。你的专属 key 是：beeps_", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheAgentsPageListsAgentsAndConfirmsDestructiveActions()
    {
        var user = WebAuth.ArrangeUser(factory);
        factory.Agents.ListAgentsAsync(user.Id, Arg.Any<CancellationToken>()).Returns(
            [new AgentRow(Guid.NewGuid(), "tom.claude", "tom's Claude", "Claude Opus 4.5", TestData.Now, null, "beeps_abc123", TestData.Now, TestData.Now)]);
        using var client = await WebAuth.SignInAsync(factory, user);

        var html = await client.GetStringAsync(new Uri("/agents", UriKind.Relative), TestSupport.Ct);

        Assert.Contains("tom&#x27;s Claude", html, StringComparison.Ordinal);
        Assert.Contains("<code data-key-prefix=\"beeps_abc123\">beeps_abc123…</code>", html, StringComparison.Ordinal);
        Assert.Contains("<time datetime=\"2026-10-16T08:30:00Z\">2026-10-16T08:30:00Z</time>", html, StringComparison.Ordinal);
        Assert.Contains("data-confirm=\"Delete this agent?", html, StringComparison.Ordinal);
        PageAssert.NoInlineScriptsOrStyles(html);
    }

    [Fact]
    public async Task DisablingRedirectsWithANotice()
    {
        var user = WebAuth.ArrangeUser(factory);
        var agent = new Actor { Id = Guid.NewGuid(), Kind = ActorKind.Agent, OwnerId = user.Id };
        factory.Agents.FindAgentAsync(agent.Id, Arg.Any<CancellationToken>()).Returns(agent);
        using var client = await WebAuth.SignInAsync(factory, user);

        using var response = await WebAuth.PostFormAsync(client, "/agents", $"/agents?handler=Disable&id={agent.Id}");
        var html = await client.GetStringAsync(new Uri("/agents", UriKind.Relative), TestSupport.Ct);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.NotNull(agent.DisabledAt);
        Assert.Contains("Agent disabled.", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheAdminAreaIsForAdminsOnly()
    {
        var user = WebAuth.ArrangeUser(factory);
        using var client = await WebAuth.SignInAsync(factory, user);

        using var response = await client.GetAsync(new Uri("/admin/users", UriKind.Relative), TestSupport.Ct);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Contains("Not allowed", await response.Content.ReadAsStringAsync(TestSupport.Ct), StringComparison.Ordinal);
    }

    [Fact]
    public async Task AdminsSeeTheUserList()
    {
        var admin = WebAuth.ArrangeUser(factory, admin: true);
        factory.Admin.ListUsersAsync(null, null, null, AdminService.PageSize, Arg.Any<CancellationToken>()).Returns(new CursorPage<AdminUserRow>(
            [new AdminUserRow(admin.Id, "tom", "tom", "tom@example.com", true, true, TestData.Now, null, null)],
            Guid.NewGuid()));
        using var client = await WebAuth.SignInAsync(factory, admin);

        var html = await client.GetStringAsync(new Uri("/admin/users", UriKind.Relative), TestSupport.Ct);

        Assert.Contains("tom@example.com", html, StringComparison.Ordinal);
        Assert.Contains("aria-current=\"page\"", html, StringComparison.Ordinal);
        Assert.Contains("before=", html, StringComparison.Ordinal);
        // The account menu opens the admin area on its overview.
        Assert.Contains("href=\"/admin\"><span class=\"icon\" aria-hidden=\"true\">admin_panel_settings</span>Admin</a>", html, StringComparison.Ordinal);
        PageAssert.NoInlineScriptsOrStyles(html);
    }

    [Fact]
    public async Task AdminRightsAreRecheckedOnEveryRequest()
    {
        var admin = WebAuth.ArrangeUser(factory, admin: true);
        using var client = await WebAuth.SignInAsync(factory, admin);

        admin.IsAdmin = false;
        using var response = await client.GetAsync(new Uri("/admin/users", UriKind.Relative), TestSupport.Ct);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task AdminSettingsShowANewInviteOnce()
    {
        var admin = WebAuth.ArrangeUser(factory, admin: true);
        factory.Admin.ListInvitesAsync(default, TestSupport.Ct).ReturnsForAnyArgs([]);
        using var client = await WebAuth.SignInAsync(factory, admin);

        using var response = await WebAuth.PostFormAsync(client, "/admin/settings", "/admin/settings?handler=CreateInvite");
        var html = await response.Content.ReadAsStringAsync(TestSupport.Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Matches("<code>[0-9A-Z]{4}-[0-9A-Z]{4}-[0-9A-Z]{4}-[0-9A-Z]{4}</code>", html);
        Assert.Contains("value=\"Open\" checked=\"checked\"", html, StringComparison.Ordinal);
        factory.Admin.Received(1).AddAuditLog(Arg.Is<AdminAuditLog>(l => l.Action == AuditActions.CreateInvite && l.AdminId == admin.Id));
        // Kept when the page is drawn again live, and other admins' pages hear of it.
        // Kept while the invite is still open (data-live-slot-requires names its row on this page).
        Assert.Matches("<div data-live-slot=\"new-invite\" data-live-slot-check=\"/admin/settings\" data-live-slot-requires=\"\\[data-invite-open=&quot;[0-9a-f-]{36}&quot;\\]\">\\s*<div class=\"alert alert-success\"", html);
        Assert.Single(TestData.LiveEvents(factory.LiveEvents, LiveEventTypes.AdminChanged));
    }

    [Fact]
    public async Task BanningFromTheDetailsPageReportsTheOutcome()
    {
        var admin = WebAuth.ArrangeUser(factory, admin: true);
        var target = WebAuth.ArrangeUser(factory, handle: "spammer");
        factory.Admin.FindUserAsync(target.Id, Arg.Any<CancellationToken>()).Returns(target);
        factory.Admin.GetUserAsync(target.Id, Arg.Any<CancellationToken>()).Returns(
            new AdminUserRow(target.Id, "spammer", "spammer", "spammer@example.com", false, true, TestData.Now, null, null));
        factory.Agents.DisableAllAsync(default, default, TestSupport.Ct).ReturnsForAnyArgs([]);
        using var client = await WebAuth.SignInAsync(factory, admin);

        using var response = await WebAuth.PostFormAsync(client, $"/admin/users/{target.Id}", $"/admin/users/{target.Id}?handler=Ban");
        var html = await client.GetStringAsync(new Uri($"/admin/users/{target.Id}", UriKind.Relative), TestSupport.Ct);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal(TestData.Now.Year, target.BannedAt?.Year);
        Assert.Contains("User banned.", html, StringComparison.Ordinal);
    }

    public void Dispose() => factory.Dispose();

    [GeneratedRegex("beeps_[0-9A-Za-z]{43}")]
    private static partial Regex KeyPattern();
}
