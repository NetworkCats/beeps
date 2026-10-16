using Beeps.Features.Accounts;
using Microsoft.AspNetCore.Authentication;

namespace Beeps.Features.Auth;

public static class SignIn
{
    public static Task SignInHumanAsync(this HttpContext context, AccountSession session) =>
        context.SignInAsync(AuthSchemes.Cookie, Principals.ForHuman(session), new AuthenticationProperties { IsPersistent = true });

    public static Task SignOutHumanAsync(this HttpContext context) => context.SignOutAsync(AuthSchemes.Cookie);
}
