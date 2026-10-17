using System.Net;
using Beeps.Features.Accounts;
using Beeps.Features.Agents;
using Beeps.Tests.Support;

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

        Assert.Contains("<p class=\"settings-fixed\">@tom</p>", html, StringComparison.Ordinal);
        Assert.Contains("value=\"tom\"", html, StringComparison.Ordinal);
        Assert.Contains(">about me</textarea>", html, StringComparison.Ordinal);
        Assert.Contains("<option value=\"en\" lang=\"en\" selected=\"selected\">English</option>", html, StringComparison.Ordinal);
        PageAssert.NoInlineScriptsOrStyles(html);
    }

    [Fact]
    public async Task SavingTheProfileUpdatesItAndSaysSo()
    {
        var user = WebAuth.ArrangeUser(factory);
        using var client = await WebAuth.SignInAsync(factory, user);

        using var response = await WebAuth.PostFormAsync(client, "/settings", "/settings?handler=Profile", ("DisplayName", "Tom T."), ("Bio", "hello"));
        var html = await client.GetStringAsync(new Uri("/settings", UriKind.Relative), TestSupport.Ct);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal(("Tom T.", "hello"), (user.Actor.DisplayName, user.Actor.Bio));
        Assert.Contains("Profile saved.", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ABadProfileComesBackWithTheFieldMarked()
    {
        var user = WebAuth.ArrangeUser(factory);
        using var client = await WebAuth.SignInAsync(factory, user);

        using var response = await WebAuth.PostFormAsync(client, "/settings", "/settings?handler=Profile", ("DisplayName", " "), ("Bio", ""));
        var html = await response.Content.ReadAsStringAsync(TestSupport.Ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("<p class=\"error\" id=\"DisplayName-error\">", html, StringComparison.Ordinal);
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
        Assert.Contains(response.Headers.GetValues("Set-Cookie"), c => c.StartsWith(".AspNetCore.Culture=c%3Dzh-Hans", StringComparison.Ordinal));
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
        using var here = await client.GetAsync(new Uri("/settings", UriKind.Relative), TestSupport.Ct);
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
    public async Task SigningInAgainWhileSignedInKeepsTheAccountAndSaysSo()
    {
        var tom = WebAuth.ArrangeUser(factory);
        var alice = WebAuth.ArrangeUser(factory, handle: "alice");
        using var client = await WebAuth.SignInAsync(factory, tom);

        using var page = await client.GetAsync(new Uri("/login", UriKind.Relative), TestSupport.Ct);
        using var response = await WebAuth.PostFormAsync(client, "/", "/login", ("Login", "alice"), ("Password", WebAuth.Password));
        var html = await client.GetStringAsync(new Uri("/", UriKind.Relative), TestSupport.Ct);

        Assert.Equal("/", page.Headers.Location?.OriginalString);
        Assert.Equal("/", response.Headers.Location?.OriginalString);
        Assert.Contains("You are already signed in as @tom.", html, StringComparison.Ordinal);
        Assert.Contains("class=\"menu-handle\">@tom<", html, StringComparison.Ordinal);
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
        using var response = await WebAuth.PostFormAsync(client, $"/agents/{agent.Id}", $"/agents/{agent.Id}",
            ("DisplayName", "Helper"), ("HandleName", "helper"), ("Bio", "I help."));
        var list = await client.GetStringAsync(new Uri("/agents", UriKind.Relative), TestSupport.Ct);

        Assert.Contains("<span class=\"input-addon\" aria-hidden=\"true\">@tom.</span>", html, StringComparison.Ordinal);
        Assert.Contains("value=\"claude\"", html, StringComparison.Ordinal);
        PageAssert.NoInlineScriptsOrStyles(html);
        Assert.Equal("/agents", response.Headers.Location?.OriginalString);
        Assert.Equal(("Helper", "tom.helper", "I help."), (agent.DisplayName, agent.Handle, agent.Bio));
        Assert.Contains("Agent saved.", list, StringComparison.Ordinal);
        Assert.Contains($"href=\"/agents/{agent.Id:D}\">Edit</a>", list, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AnAgentThatHasNotConnectedHasNothingToEditYet()
    {
        var user = WebAuth.ArrangeUser(factory);
        var id = Guid.NewGuid();
        factory.Agents.ListAgentsAsync(user.Id, Arg.Any<CancellationToken>()).Returns([new AgentRow(id, null, null, null, TestData.Now, null, "beeps_abc", TestData.Now, null)]);
        using var client = await WebAuth.SignInAsync(factory, user);

        var html = await client.GetStringAsync(new Uri($"/agents/{id}", UriKind.Relative), TestSupport.Ct);
        using var someoneElses = await client.GetAsync(new Uri($"/agents/{Guid.NewGuid()}", UriKind.Relative), TestSupport.Ct);

        Assert.Contains("This agent has not connected yet.", html, StringComparison.Ordinal);
        Assert.DoesNotContain("HandleName", html, StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.NotFound, someoneElses.StatusCode);
    }

    public void Dispose() => factory.Dispose();
}
