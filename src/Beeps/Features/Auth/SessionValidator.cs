using System.Security.Claims;
using Beeps.Features.Accounts;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;

namespace Beeps.Features.Auth;

/// <summary>
/// Re-reads the account behind a session cookie on every request. A ban, a deletion or a password reset
/// (new security stamp) ends the session at once, and admin rights are never older than the request
/// (PLAN 3.6).
/// </summary>
public static class SessionValidator
{
    public static async Task ValidateAsync(CookieValidatePrincipalContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var principal = context.Principal;
        var stamp = principal?.FindFirstValue(BeepsClaims.SecurityStamp);
        AccountSession? session = null;
        if (Guid.TryParse(principal?.FindFirstValue(ClaimTypes.NameIdentifier), out var userId) && stamp is not null)
        {
            var accounts = context.HttpContext.RequestServices.GetRequiredService<AccountService>();
            session = await accounts.GetSessionAsync(userId, context.HttpContext.RequestAborted);
        }

        if (session is null || !string.Equals(session.SecurityStamp, stamp, StringComparison.Ordinal))
        {
            context.RejectPrincipal();
            await context.HttpContext.SignOutAsync(AuthSchemes.Cookie);
            return;
        }

        context.ReplacePrincipal(Principals.ForHuman(session));
    }
}
