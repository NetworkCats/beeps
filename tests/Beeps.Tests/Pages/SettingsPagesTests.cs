using System.Net;
using Beeps.Features.Accounts;
using Beeps.Features.Agents;
using Beeps.Features.Shared;
using Beeps.Tests.Support;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.DependencyInjection;

namespace Beeps.Tests.Pages;

public sealed class SettingsPagesTests : IDisposable
{
    private readonly BeepsWebApplicationFactory factory = new();

    [Fact]
    public async Task SettingsShowTheProfileAndTheLanguage()
    {
        var user = WebAuth.ArrangeUser(factory);
        user.Actor.Bio = "about me";
        using var client = await WebAuth.SignInAsync(factory, user);

        var html = await client.GetStringAsync(new Uri("/settings", UriKind.Relative), TestSupport.Ct);
        var page = await Islands.PageAsync(client, "/settings");

        var settings = Islands.Props(page, "settings");
        Assert.Equal("tom", settings.GetProperty("handle").GetString());
        Assert.Equal("tom", settings.GetProperty("displayName").GetString());
        Assert.Equal("about me", settings.GetProperty("bio").GetString());
        // English chosen, among the languages offered under their own names.
        Assert.Equal("en", settings.GetProperty("culture").GetString());
        Assert.Contains(settings.GetProperty("cultures").EnumerateArray(), c => c.GetProperty("value").GetString() == "en" && c.GetProperty("label").GetString() == "English");
        PageAssert.NoInlineScriptsOrStyles(html);
    }

    [Theory]
    [InlineData("/settings", null)]
    [InlineData("/settings/profile", "profile")]
    [InlineData("/settings/account", "account")]
    [InlineData("/settings/email", "email")]
    [InlineData("/settings/password", "password")]
    [InlineData("/settings/display", "display")]
    public async Task EachSectionHasAPageOfItsOwn(string url, string? section)
    {
        var user = WebAuth.ArrangeUser(factory);
        using var client = await WebAuth.SignInAsync(factory, user);

        var page = await Islands.PageAsync(client, url);

        var settings = Islands.Props(page, "settings");
        Assert.Equal(section, settings.TryGetProperty("section", out var shown) ? shown.GetString() : null);
        Assert.Equal("tom@example.com", settings.GetProperty("email").GetString());
    }

    [Fact]
    public async Task ThereAreNoOtherSections()
    {
        var user = WebAuth.ArrangeUser(factory);
        using var client = await WebAuth.SignInAsync(factory, user);

        using var response = await client.GetAsync(new Uri("/settings/notifications", UriKind.Relative), TestSupport.Ct);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Theory]
    [InlineData("Profile", "/settings/profile")]
    [InlineData("Theme", "/settings/display")]
    [InlineData("Language", "/settings/display")]
    [InlineData("Privacy", "/settings/privacy")]
    public async Task AFormComesBackToItsSection(string handler, string section)
    {
        var user = WebAuth.ArrangeUser(factory);
        using var client = await WebAuth.SignInAsync(factory, user);

        // Sent from the list, which has no forms: still back on the section it belongs to.
        using var response = await WebAuth.PostFormAsync(client, "/settings", $"/settings?handler={handler}",
            ("DisplayName", "Tom"), ("Bio", ""), ("Theme", "dark"), ("Culture", "en"));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal(section, response.Headers.Location?.OriginalString);
    }

    [Fact]
    public async Task ARefusedFormIsShownOnItsSection()
    {
        var user = WebAuth.ArrangeUser(factory);
        using var client = await WebAuth.SignInAsync(factory, user);

        using var response = await WebAuth.PostFormAsync(client, "/settings/password", "/settings/password?handler=Password",
            ("CurrentPassword", "not it"), ("NewPassword", "a brand new password"));
        var html = await response.Content.ReadAsStringAsync(TestSupport.Ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("password", Islands.Props(html, "settings").GetProperty("section").GetString());
    }

    [Fact]
    public async Task SavingTheProfileUpdatesItAndSaysSo()
    {
        var user = WebAuth.ArrangeUser(factory);
        using var client = await WebAuth.SignInAsync(factory, user);

        using var response = await WebAuth.PostFormAsync(client, "/settings", "/settings?handler=Profile", ("DisplayName", "Tom T."), ("Bio", "hello"));
        var page = await Islands.PageAsync(client, "/settings");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal(("Tom T.", "hello"), (user.Actor.DisplayName, user.Actor.Bio));
        Assert.Contains("Profile saved.", page, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ThePrivacySwitchesSaveWhatIsOnAndShowIt()
    {
        var user = WebAuth.ArrangeUser(factory);
        using var client = await WebAuth.SignInAsync(factory, user);

        // An unchecked switch is not sent at all.
        using var response = await WebAuth.PostFormAsync(client, "/settings/privacy", "/settings/privacy?handler=Privacy", ("HideLikes", "true"), ("HideFollowCounts", "true"));
        var page = await Islands.PageAsync(client, "/settings/privacy");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal((true, false, true), (user.HideLikes, user.HideFollows, user.HideFollowCounts));
        var privacy = Islands.Props(page, "settings").GetProperty("privacy");
        Assert.True(privacy.GetProperty("hideLikes").GetBoolean());
        Assert.False(privacy.GetProperty("hideFollows").GetBoolean());
        Assert.True(privacy.GetProperty("hideFollowCounts").GetBoolean());
    }

    [Fact]
    public async Task ABadProfileComesBackWithTheFieldMarked()
    {
        var user = WebAuth.ArrangeUser(factory);
        using var client = await WebAuth.SignInAsync(factory, user);

        using var response = await WebAuth.PostFormAsync(client, "/settings", "/settings?handler=Profile", ("DisplayName", " "), ("Bio", ""));
        var html = await response.Content.ReadAsStringAsync(TestSupport.Ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.True(Islands.Envelope(html, "settings").GetProperty("errors").TryGetProperty("DisplayName", out _));
        Assert.Equal("tom", user.Actor.DisplayName);
    }

    [Fact]
    public async Task ChangingTheLanguageSavesItToTheAccount()
    {
        var user = WebAuth.ArrangeUser(factory);
        using var client = await WebAuth.SignInAsync(factory, user);

        using var response = await WebAuth.PostFormAsync(client, "/settings", "/settings?handler=Language", ("Culture", "zh-Hans"));
        var html = await client.GetStringAsync(new Uri("/settings", UriKind.Relative), TestSupport.Ct);

        Assert.Equal("zh-Hans", user.Locale);
        Assert.Contains(response.Headers.GetValues("Set-Cookie"), c => c.StartsWith("beeps.lang=c%3Dzh-Hans", StringComparison.Ordinal));
        Assert.Contains("<html lang=\"zh-Hans\">", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ChangingThePasswordKeepsThisSessionAndEndsTheOthers()
    {
        var user = WebAuth.ArrangeUser(factory);
        using var elsewhere = await WebAuth.SignInAsync(factory, user);
        using var client = await WebAuth.SignInAsync(factory, user);

        using var response = await WebAuth.PostFormAsync(client, "/settings", "/settings?handler=Password",
            ("CurrentPassword", WebAuth.Password), ("NewPassword", "a brand new password"));
        using var here = await Islands.GetAsync(client, "/settings");
        using var there = await elsewhere.GetAsync(new Uri("/settings", UriKind.Relative), TestSupport.Ct);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal(HttpStatusCode.OK, here.StatusCode);
        Assert.Contains("Password changed.", await here.Content.ReadAsStringAsync(TestSupport.Ct), StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.Redirect, there.StatusCode);
        Assert.StartsWith("http://localhost/login", there.Headers.Location?.OriginalString, StringComparison.Ordinal);
        Assert.Equal(1, factory.Sessions.All.Count(s => s.UserId == user.Id && s.RevokedAt is null));
    }

    [Fact]
    public async Task AWrongCurrentPasswordIsShownOnTheForm()
    {
        var user = WebAuth.ArrangeUser(factory);
        var hash = user.PasswordHash;
        using var client = await WebAuth.SignInAsync(factory, user);

        using var response = await WebAuth.PostFormAsync(client, "/settings", "/settings?handler=Password",
            ("CurrentPassword", "not it"), ("NewPassword", "a brand new password"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("The current password is not correct.", await response.Content.ReadAsStringAsync(TestSupport.Ct), StringComparison.Ordinal);
        Assert.Equal(hash, user.PasswordHash);
    }

    [Fact]
    public async Task ChangingTheEmailAsksForTheCodeSentToTheNewAddressThenMovesTheAccount()
    {
        var user = WebAuth.ArrangeUser(factory);
        EmailCode? stored = null;
        factory.Accounts.When(s => s.AddEmailCode(Arg.Any<EmailCode>())).Do(ci => stored = ci.Arg<EmailCode>());
        factory.Accounts.FindEmailCodeAsync(EmailCodePurpose.ChangeEmail, "tom@example.com", Arg.Any<CancellationToken>()).Returns(_ => stored);
        factory.Accounts.TryCountCodeAttemptAsync(default, default!, default, default, TestSupport.Ct).ReturnsForAnyArgs(true);
        using var client = await WebAuth.SignInAsync(factory, user);

        using var asked = await WebAuth.PostFormAsync(client, "/settings/email", "/settings/email?handler=Email",
            ("NewEmail", "new@example.org"), ("CurrentPassword", WebAuth.Password));
        var waiting = Islands.Props(await Islands.PageAsync(client, "/settings/email"), "settings");
        var code = factory.Services.GetRequiredService<IDataProtectionProvider>().CreateProtector(AccountCodeEmail.ProtectorPurpose)
            .Unprotect(SentCode().ProtectedCode);
        using var confirmed = await WebAuth.PostFormAsync(client, "/settings/email", "/settings/email?handler=ConfirmEmail", ("Code", code));
        var account = await Islands.PageAsync(client, "/settings/account");

        Assert.Equal("/settings/email", asked.Headers.Location?.OriginalString);
        Assert.Equal("new@example.org", waiting.GetProperty("pendingEmail").GetString());
        Assert.Equal("tom@example.com", waiting.GetProperty("email").GetString());
        Assert.Equal("/settings/account", confirmed.Headers.Location?.OriginalString);
        Assert.Equal(("new@example.org", null), (user.NormalizedEmail, user.PendingEmail));
        Assert.Contains("Email changed.", account, StringComparison.Ordinal);
        Assert.Equal("new@example.org", Islands.Props(account, "settings").GetProperty("email").GetString());
    }

    [Fact]
    public async Task AWrongCodeIsShownOnTheEmailPage()
    {
        var user = WebAuth.ArrangeUser(factory);
        user.PendingEmail = "new@example.org";
        using var client = await WebAuth.SignInAsync(factory, user);

        using var response = await WebAuth.PostFormAsync(client, "/settings/email", "/settings/email?handler=ConfirmEmail", ("Code", "123456"));
        var html = await response.Content.ReadAsStringAsync(TestSupport.Ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("email", Islands.Props(html, "settings").GetProperty("section").GetString());
        Assert.True(Islands.Envelope(html, "settings").GetProperty("errors").TryGetProperty("Code", out _));
        Assert.Equal("tom@example.com", user.NormalizedEmail);
    }

    [Fact]
    public async Task TheCodeForANewAddressCanBeSentAgainOnceAMinuteHasPassed()
    {
        var user = WebAuth.ArrangeUser(factory);
        user.PendingEmail = "new@example.org";
        var code = new EmailCode { Purpose = EmailCodePurpose.ChangeEmail, NormalizedEmail = "tom@example.com", CodeHash = "x", CreatedAt = TestData.Now, ExpiresAt = DateTimeOffset.MaxValue };
        factory.Accounts.FindEmailCodeAsync(EmailCodePurpose.ChangeEmail, "tom@example.com", Arg.Any<CancellationToken>()).Returns(code);
        using var client = await WebAuth.SignInAsync(factory, user);

        var page = Islands.Props(await Islands.PageAsync(client, "/settings/email"), "settings");
        code.CreatedAt = DateTimeOffset.UtcNow - TimeSpan.FromMinutes(2);
        using var resent = await WebAuth.PostFormAsync(client, "/settings/email", "/settings/email?handler=ResendEmail");

        Assert.Equal(TestData.Now + AccountService.ResendInterval, page.GetProperty("resendAt").GetDateTimeOffset());
        Assert.Equal("/settings/email", resent.Headers.Location?.OriginalString);
        Assert.Equal("new@example.org", SentCode().To);
    }

    [Fact]
    public async Task SigningInAgainWhileSignedInKeepsTheAccountAndSaysSo()
    {
        var tom = WebAuth.ArrangeUser(factory);
        var alice = WebAuth.ArrangeUser(factory, handle: "alice");
        using var client = await WebAuth.SignInAsync(factory, tom);

        using var page = await client.GetAsync(new Uri("/login", UriKind.Relative), TestSupport.Ct);
        using var response = await WebAuth.PostFormAsync(client, "/", "/login", ("Login", "alice"), ("Password", WebAuth.Password));
        var html = await client.GetStringAsync(new Uri("/", UriKind.Relative), TestSupport.Ct);
        var data = await Islands.PageAsync(client, "/");

        Assert.Equal("/", page.Headers.Location?.OriginalString);
        Assert.Equal("/", response.Headers.Location?.OriginalString);
        Assert.Contains("You are already signed in as @tom.", data, StringComparison.Ordinal);
        // Still tom's account menu.
        Assert.Contains("account-menu", Islands.Names(html));
        Assert.Equal("tom", Islands.Config(data).GetProperty("viewer").GetProperty("handle").GetString());
        Assert.DoesNotContain(factory.Sessions.All, s => s.UserId == alice.Id);
    }

    [Fact]
    public async Task TheAgentPageEditsTheNameAfterTheOwnersHandle()
    {
        var user = WebAuth.ArrangeUser(factory);
        var agent = new Actor
        {
            Id = Guid.NewGuid(), Kind = ActorKind.Agent, OwnerId = user.Id, Handle = "tom.claude", NormalizedHandle = "tom.claude",
            DisplayName = "tom's Claude", ModelName = "Claude", CreatedAt = TestData.Now,
        };
        factory.Agents.FindAgentAsync(agent.Id, Arg.Any<CancellationToken>()).Returns(agent);
        factory.Agents.ListAgentsAsync(user.Id, Arg.Any<CancellationToken>()).Returns(_ =>
            [new AgentRow(agent.Id, agent.Handle, agent.DisplayName, agent.ModelName, TestData.Now, null, "beeps_abc", TestData.Now, null, agent.Bio)]);
        using var client = await WebAuth.SignInAsync(factory, user);

        var html = await client.GetStringAsync(new Uri($"/agents/{agent.Id}", UriKind.Relative), TestSupport.Ct);
        var page = await Islands.PageAsync(client, $"/agents/{agent.Id}");
        using var response = await WebAuth.PostFormAsync(client, $"/agents/{agent.Id}", $"/agents/{agent.Id}",
            ("DisplayName", "Helper"), ("HandleName", "helper"), ("Bio", "I help."));
        var list = await Islands.PageAsync(client, "/agents");

        // The name after "@tom.", which is fixed.
        var edit = Islands.Props(page, "agent");
        Assert.Equal("tom", edit.GetProperty("ownerHandle").GetString());
        Assert.Equal("claude", edit.GetProperty("handleName").GetString());
        PageAssert.NoInlineScriptsOrStyles(html);
        Assert.Equal("/agents", response.Headers.Location?.OriginalString);
        Assert.Equal(("Helper", "tom.helper", "I help."), (agent.DisplayName, agent.Handle, agent.Bio));
        Assert.Contains("Agent saved.", list, StringComparison.Ordinal);
        // Connected, so the list offers to edit it.
        var row = Islands.Props(list, "agents").GetProperty("agents").EnumerateArray().Single();
        Assert.Equal(agent.Id, row.GetProperty("id").GetGuid());
        Assert.True(row.GetProperty("connected").GetBoolean());
    }

    [Fact]
    public async Task AnAgentThatHasNotConnectedHasNothingToEditYet()
    {
        var user = WebAuth.ArrangeUser(factory);
        var id = Guid.NewGuid();
        factory.Agents.ListAgentsAsync(user.Id, Arg.Any<CancellationToken>()).Returns([new AgentRow(id, null, null, null, TestData.Now, null, "beeps_abc", TestData.Now, null)]);
        using var client = await WebAuth.SignInAsync(factory, user);

        var data = await Islands.PageAsync(client, $"/agents/{id}");
        using var someoneElses = await Islands.GetAsync(client, $"/agents/{Guid.NewGuid()}");

        // The page says so (AgentEdit.tsx) and offers no form.
        Assert.False(Islands.Props(data, "agent").GetProperty("agent").TryGetProperty("connected", out _));
        Assert.DoesNotContain("HandleName", data, StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.NotFound, someoneElses.StatusCode);
    }

    public void Dispose() => factory.Dispose();

    private AccountCodeEmail SentCode() =>
        factory.Outbox.ReceivedCalls()
            .Where(c => c.GetMethodInfo().Name == nameof(IOutbox.Enqueue) && (string)c.GetArguments()[0]! == AccountCodeEmail.MessageType)
            .Select(c => (AccountCodeEmail)c.GetArguments()[1]!)
            .Last();
}
