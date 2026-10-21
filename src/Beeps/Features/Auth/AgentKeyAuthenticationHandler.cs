using System.Text.Encodings.Web;
using Beeps.Features.Agents;
using Beeps.Features.RateLimiting;
using Beeps.Features.Shared;
using Beeps.Features.Shared.Http;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Options;
using Microsoft.Net.Http.Headers;

namespace Beeps.Features.Auth;

/// <summary><c>Authorization: Bearer beeps_…</c> for the agent API. Challenges and denials are ProblemDetails.</summary>
public sealed class AgentKeyAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    AgentService agents,
    RateLimitService rateLimits,
    IStringLocalizer<SharedResource> localizer) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    private const string BearerPrefix = "Bearer ";

    /// <summary>Set when wrong keys from this address are past their limit: the key was not looked up.</summary>
    private Error? refused;

    /// <summary>
    /// Authentication runs before the rate limits, and every key nobody has costs a database query: so wrong keys are
    /// counted per address, and past their limit no key from it is looked up.
    /// </summary>
    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var header = Request.Headers.Authorization.ToString();
        if (!header.StartsWith(BearerPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return AuthenticateResult.NoResult();
        }

        var partition = RateLimitPartitions.Ip(Context.Connection.RemoteIpAddress?.ToString());
        if (await rateLimits.WaitAsync(RateLimitPolicies.AgentKey, partition, Context.RequestAborted) is var wait && wait > TimeSpan.Zero)
        {
            refused = Error.RateLimited(wait, localizer["RateLimit.Detail"]);
            return AuthenticateResult.Fail("Too many invalid agent keys.");
        }

        var identity = await agents.AuthenticateAsync(header[BearerPrefix.Length..].Trim(), Context.RequestAborted);
        if (identity is null)
        {
            await rateLimits.CheckAsync(RateLimitPolicies.AgentKey, partition, Context.RequestAborted);
            return AuthenticateResult.Fail("Invalid agent key.");
        }

        return AuthenticateResult.Success(new AuthenticationTicket(Principals.ForAgent(identity), Scheme.Name));
    }

    protected override async Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        var result = await HandleAuthenticateOnceSafeAsync();
        if (refused is not null)
        {
            await refused.ToHttpResult().ExecuteAsync(Context);
            return;
        }

        Response.Headers[HeaderNames.WWWAuthenticate] = "Bearer";
        var detail = result.Failure is null ? localizer["Error.AgentKeyRequired"] : localizer["Error.AgentKeyInvalid"];
        await Error.Unauthorized(ErrorCodes.Unauthorized, detail).ToHttpResult().ExecuteAsync(Context);
    }

    // The only thing a valid key can be refused for is skipping hello, so say exactly that.
    protected override Task HandleForbiddenAsync(AuthenticationProperties properties) =>
        (Context.User.HasClaim(BeepsClaims.Connected, "false")
            ? Error.Forbidden(AgentErrorCodes.AgentNotConnected, localizer["Error.AgentNotConnected"])
            : Error.Forbidden(ErrorCodes.Forbidden, localizer["Error.Forbidden"])).ToHttpResult().ExecuteAsync(Context);
}
