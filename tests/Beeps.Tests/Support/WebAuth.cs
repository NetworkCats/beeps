using System.Net;
using Beeps.Features.Accounts;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Beeps.Tests.Support;

/// <summary>Signs a test user in through the real login page, against the factory's mocked account store.</summary>
internal static class WebAuth
{
    public const string Password = "correct horse";

    public static User ArrangeUser(BeepsWebApplicationFactory factory, string handle = "tom", bool verified = true, bool admin = false, string locale = "en")
    {
        var user = TestData.User(handle, admin, verified);
        user.Locale = locale;
        user.PasswordHash = new PasswordHasher<User>().HashPassword(user, Password);
        factory.Accounts.FindUserByHandleAsync(user.Actor.NormalizedHandle!, Arg.Any<CancellationToken>()).Returns(user);
        factory.Accounts.FindUserAsync(user.Id, Arg.Any<CancellationToken>()).Returns(user);
        // Read on every request, so a test can change the user (ban, rights…) after signing in.
        factory.Accounts.GetAccountStateAsync(user.Id, Arg.Any<CancellationToken>()).Returns(_ => user.BannedAt is null
            ? new AccountState(user.Id, user.Actor.Handle!, user.Actor.DisplayName!, user.IsAdmin, user.EmailVerifiedAt is not null, user.Locale)
            : null);
        if (admin)
        {
            factory.Admin.IsActiveAdminAsync(user.Id, Arg.Any<CancellationToken>()).Returns(true);
        }

        return user;
    }

    public static HttpClient NewClient(BeepsWebApplicationFactory factory) =>
        factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

    public static async Task<HttpClient> SignInAsync(BeepsWebApplicationFactory factory, User user)
    {
        var client = NewClient(factory);
        var html = await client.GetStringAsync(new Uri("/login", UriKind.Relative), TestSupport.Ct);
        using var form = new FormUrlEncodedContent(
        [
            KeyValuePair.Create("Login", user.Actor.Handle!),
            KeyValuePair.Create("Password", Password),
            KeyValuePair.Create("__RequestVerificationToken", TestSupport.AntiforgeryToken(html)),
        ]);
        using var response = await client.PostAsync(new Uri("/login", UriKind.Relative), form, TestSupport.Ct);
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        return client;
    }

    public static async Task<HttpResponseMessage> PostFormAsync(HttpClient client, string pageUrl, string postUrl, params (string Name, string Value)[] fields)
    {
        var html = await client.GetStringAsync(new Uri(pageUrl, UriKind.Relative), TestSupport.Ct);
        using var form = new FormUrlEncodedContent(
            [.. fields.Select(f => KeyValuePair.Create(f.Name, f.Value)), KeyValuePair.Create("__RequestVerificationToken", TestSupport.AntiforgeryToken(html))]);
        return await client.PostAsync(new Uri(postUrl, UriKind.Relative), form, TestSupport.Ct);
    }
}
