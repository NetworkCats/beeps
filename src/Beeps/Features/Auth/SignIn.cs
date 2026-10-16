using Beeps.Features.Accounts;
using Microsoft.AspNetCore.Authentication;

namespace Beeps.Features.Auth;

public static class SignIn
{
    /// <summary>Starts a server-side session (through <see cref="ServerSessionTicketStore"/>) and sets its cookie.</summary>
    public static Task SignInHumanAsync(this HttpContext context, AccountState account) =>
        context.SignInAsync(AuthSchemes.Cookie, Principals.ForHuman(account), new AuthenticationProperties { IsPersistent = true });

    /// <summary>Revokes the server-side session and clears the cookie.</summary>
    public static Task SignOutHumanAsync(this HttpContext context) => context.SignOutAsync(AuthSchemes.Cookie);
}
