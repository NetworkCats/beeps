using Beeps.Features.Accounts;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;

namespace Beeps.Features.Auth;

/// <summary>
/// Makes the session cookie hold only a session token (PLAN 6.14). The session lives on the server
/// (<see cref="SessionService"/>); on every request the principal is rebuilt from the current account
/// state, so a ban, a password reset or a change of admin rights applies to the very next request.
/// </summary>
public sealed class ServerSessionTicketStore : ITicketStore
{
    public async Task<string> StoreAsync(AuthenticationTicket ticket, HttpContext httpContext, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(ticket);
        ArgumentNullException.ThrowIfNull(httpContext);
        var client = new SessionClient(httpContext.Request.Headers.UserAgent.ToString(), httpContext.Connection.RemoteIpAddress?.ToString());
        return await Sessions(httpContext).StartAsync(ticket.Principal.ActorId(), client, cancellationToken);
    }

    public async Task<AuthenticationTicket?> RetrieveAsync(string key, HttpContext httpContext, CancellationToken cancellationToken)
    {
        var session = await Sessions(httpContext).ResolveAsync(key, cancellationToken);
        if (session is null)
        {
            return null;
        }

        // The cookie handler re-issues the cookie only for a ticket past half its life. The browser cookie
        // must last as long as the server session, which slides on use, so a session that was just
        // extended is reported as due for re-issue and any other as fresh.
        var properties = new AuthenticationProperties
        {
            IsPersistent = true,
            ExpiresUtc = session.ExpiresAt,
            IssuedUtc = session.ExpiresAt - (session.Renewed ? 3 : 1) * SessionService.Lifetime,
        };
        return new AuthenticationTicket(Principals.ForHuman(session.Account, session.SessionId), properties, AuthSchemes.Cookie);
    }

    // The session's expiry is already moved forward by SessionService; nothing else to store.
    public Task RenewAsync(string key, AuthenticationTicket ticket, HttpContext httpContext, CancellationToken cancellationToken) =>
        Task.CompletedTask;

    public Task RemoveAsync(string key, HttpContext httpContext, CancellationToken cancellationToken) =>
        Sessions(httpContext).EndAsync(key, cancellationToken);

    // The cookie handler always calls the HttpContext overloads; these exist only because the interface requires them.
    public Task<string> StoreAsync(AuthenticationTicket ticket) => throw new NotSupportedException();

    public Task RenewAsync(string key, AuthenticationTicket ticket) => throw new NotSupportedException();

    public Task<AuthenticationTicket?> RetrieveAsync(string key) => throw new NotSupportedException();

    public Task RemoveAsync(string key) => throw new NotSupportedException();

    private static SessionService Sessions(HttpContext httpContext)
    {
        ArgumentNullException.ThrowIfNull(httpContext);
        return httpContext.RequestServices.GetRequiredService<SessionService>();
    }
}
