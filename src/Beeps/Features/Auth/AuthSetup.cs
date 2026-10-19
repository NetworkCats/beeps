using Beeps.Features.Accounts;
using Beeps.Features.Site;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc.ApplicationModels;

namespace Beeps.Features.Auth;

public static class AuthSetup
{
    public const string LoginPath = "/login";
    public const string VerifyPath = "/onboarding/verify";

    public static IServiceCollection AddBeepsAuth(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddAuthentication(AuthSchemes.ByPath)
            .AddPolicyScheme(AuthSchemes.ByPath, null, options =>
                options.ForwardDefaultSelector = context => IsApi(context.Request) ? AuthSchemes.AgentKey : AuthSchemes.Cookie)
            .AddCookie(AuthSchemes.Cookie, options =>
            {
                options.Cookie.Name = CookieNames.Session;
                options.Cookie.HttpOnly = true;
                options.Cookie.SameSite = SameSiteMode.Lax;
                options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
                // The cookie carries only a session token; the session itself is server-side (PLAN 6.14).
                options.SessionStore = new ServerSessionTicketStore();
                options.ExpireTimeSpan = SessionService.Lifetime;
                options.SlidingExpiration = true;
                options.LoginPath = LoginPath;
                options.Events.OnRedirectToLogin = context =>
                {
                    // PLAN 6.10: the stream and what the islands fetch refuse signed-out requests outright;
                    // a redirect to the login page would only feed HTML to a script.
                    if (IsLive(context.Request))
                    {
                        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                    }
                    else
                    {
                        context.Response.Redirect(context.RedirectUri);
                    }

                    return Task.CompletedTask;
                };
                options.Events.OnRedirectToAccessDenied = context =>
                {
                    // An unverified account is sent to finish verification; anyone else gets a plain 403 page.
                    if (!IsLive(context.Request) && context.HttpContext.User.FindFirst(BeepsClaims.EmailVerified)?.Value == "false")
                    {
                        context.Response.Redirect(VerifyPath);
                    }
                    else
                    {
                        context.Response.StatusCode = StatusCodes.Status403Forbidden;
                    }

                    return Task.CompletedTask;
                };
            })
            .AddScheme<AuthenticationSchemeOptions, AgentKeyAuthenticationHandler>(AuthSchemes.AgentKey, null)
            .AddBeepsExternalLogins(configuration);

        services.AddAuthorizationBuilder()
            .AddPolicy(AuthPolicies.SignedIn, policy => policy
                .AddAuthenticationSchemes(AuthSchemes.Cookie)
                .RequireAuthenticatedUser()
                .RequireClaim(BeepsClaims.Kind, BeepsClaims.HumanKind))
            .AddPolicy(AuthPolicies.Member, policy => policy
                .AddAuthenticationSchemes(AuthSchemes.Cookie)
                .RequireAuthenticatedUser()
                .RequireClaim(BeepsClaims.Kind, BeepsClaims.HumanKind)
                .RequireClaim(BeepsClaims.EmailVerified, "true"))
            .AddPolicy(AuthPolicies.Admin, policy => policy
                .AddAuthenticationSchemes(AuthSchemes.Cookie)
                .RequireAuthenticatedUser()
                .RequireClaim(BeepsClaims.Kind, BeepsClaims.HumanKind)
                .RequireClaim(BeepsClaims.EmailVerified, "true")
                .RequireRole(BeepsClaims.AdminRole))
            .AddPolicy(AuthPolicies.Agent, policy => policy
                .AddAuthenticationSchemes(AuthSchemes.AgentKey)
                .RequireAuthenticatedUser()
                .RequireClaim(BeepsClaims.Kind, BeepsClaims.AgentKind))
            .AddPolicy(AuthPolicies.ConnectedAgent, policy => policy
                .AddAuthenticationSchemes(AuthSchemes.AgentKey)
                .RequireAuthenticatedUser()
                .RequireClaim(BeepsClaims.Kind, BeepsClaims.AgentKind)
                .RequireClaim(BeepsClaims.Connected, "true"));
        return services;
    }

    /// <summary>Which pages need which policy. Pages not listed here are public.</summary>
    public static PageConventionCollection AddBeepsAuthorization(this PageConventionCollection conventions)
    {
        ArgumentNullException.ThrowIfNull(conventions);
        // PLAN 12: the whole admin folder, so a new admin page cannot be forgotten.
        conventions.AuthorizeFolder("/Admin", AuthPolicies.Admin);
        conventions.AuthorizeFolder("/Agents", AuthPolicies.Member);
        conventions.AuthorizePage("/Onboarding/Verify", AuthPolicies.SignedIn);
        conventions.AuthorizePage("/Onboarding/Agent", AuthPolicies.Member);
        conventions.AuthorizePage("/Onboarding/Connect", AuthPolicies.Member);
        // Reading is public; everything that writes, and the account's own pages, needs a member.
        conventions.AuthorizePage("/Compose", AuthPolicies.Member);
        conventions.AuthorizePage("/Beep/Actions", AuthPolicies.Member);
        conventions.AuthorizePage("/Profiles/Follow", AuthPolicies.Member);
        conventions.AuthorizePage("/Profiles/Block", AuthPolicies.Member);
        conventions.AuthorizePage("/Beep/Report", AuthPolicies.Member);
        conventions.AuthorizePage("/Notifications", AuthPolicies.Member);
        conventions.AuthorizePage("/Settings", AuthPolicies.Member);
        return conventions;
    }

    /// <summary>The browsers' event stream and what the islands fetch (Pages/Islands/IslandEndpoints.cs): never a login redirect.</summary>
    public static bool IsLive(HttpRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return request.Path.StartsWithSegments("/stream") || request.Path.StartsWithSegments("/live");
    }

    public static bool IsApi(HttpRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return request.Path.StartsWithSegments("/api");
    }
}
