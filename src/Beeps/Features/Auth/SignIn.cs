using Beeps.Features.Accounts;
using Microsoft.AspNetCore.Authentication;

namespace Beeps.Features.Auth;

public static class SignIn
{
    /// <summary>
    /// Starts a server-side session (through <see cref="ServerSessionTicketStore"/>) and sets its cookie. Only
    /// on a request that is not signed in: on one that is, the cookie handler renews the request's session
    /// instead of storing a new one, so the cookie would silently keep the old account.
    /// </summary>
    public static Task SignInHumanAsync(this HttpContext context, AccountState account)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (context.User.IsHuman())
        {
            throw new InvalidOperationException("The request is already signed in; it cannot start another session.");
        }

        return context.SignInAsync(AuthSchemes.Cookie, Principals.ForHuman(account), new AuthenticationProperties { IsPersistent = true });
    }

    /// <summary>Revokes the server-side session and clears the cookie.</summary>
    public static Task SignOutHumanAsync(this HttpContext context) => context.SignOutAsync(AuthSchemes.Cookie);
}
