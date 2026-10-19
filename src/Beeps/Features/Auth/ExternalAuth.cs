using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text.Json;
using Beeps.Features.Accounts;
using Beeps.Features.Site;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.OAuth;

namespace Beeps.Features.Auth;

/// <summary>
/// Signing in with Google and GitHub (PLAN 6.14), with the framework's own OAuth handler: <c>/oauth/{provider}</c>
/// (Pages/Account/External) sends the browser there, it comes back to <c>/oauth/{provider}/callback</c>, and what the
/// provider said is kept in a short-lived cookie (<see cref="AuthSchemes.External"/>) for <c>/oauth/complete</c>
/// (Pages/Account/ExternalComplete) to sign in, sign up or link with. A provider without a client id and secret is not
/// set up at all: its buttons stay, and say so (local development has none).
/// </summary>
public static class ExternalAuth
{
    public const string CompletePath = "/oauth/complete";

    /// <summary>Long enough to pick a username after coming back from the provider.</summary>
    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(15);

    /// <summary>The account a link was started from (<see cref="AuthenticationProperties.Items"/>): no other may finish it.</summary>
    public const string LinkItem = "beeps.link";

    /// <summary>Where signing in leads once done, as the login page was asked for it.</summary>
    public const string ReturnItem = "beeps.return";

    /// <summary>The provider's username, as a claim of the cookie's identity.</summary>
    private const string UsernameClaim = "beeps:username";

    public static string CallbackPath(string provider) => $"/oauth/{provider}/callback";

    /// <summary>Where the browser goes when the provider's way back fails or is cancelled (Pages/Account/External).</summary>
    public static string FailedPath(string provider, bool link) => $"/oauth/{provider}?handler=Failed&link={(link ? "true" : "false")}";

    public static AuthenticationBuilder AddBeepsExternalLogins(this AuthenticationBuilder builder, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(configuration);
        builder.AddCookie(AuthSchemes.External, options =>
        {
            options.Cookie.Name = CookieNames.External;
            options.Cookie.HttpOnly = true;
            options.Cookie.SameSite = SameSiteMode.Lax;
            options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
            options.ExpireTimeSpan = Lifetime;
            options.SlidingExpiration = false;
        });

        var settings = configuration.GetSection(ExternalLoginOptions.SectionName).Get<ExternalLoginOptions>() ?? new ExternalLoginOptions();
        if (settings.Google.IsSet)
        {
            builder.AddOAuth(ExternalProviders.Google, options =>
            {
                Configure(options, ExternalProviders.Google, settings.Google);
                options.AuthorizationEndpoint = "https://accounts.google.com/o/oauth2/v2/auth";
                options.TokenEndpoint = "https://oauth2.googleapis.com/token";
                options.UserInformationEndpoint = "https://openidconnect.googleapis.com/v1/userinfo";
                options.Scope.Add("openid");
                options.Scope.Add("email");
                options.Scope.Add("profile");
                options.Events.OnCreatingTicket = ReadGoogleAsync;
            });
        }

        if (settings.GitHub.IsSet)
        {
            builder.AddOAuth(ExternalProviders.GitHub, options =>
            {
                Configure(options, ExternalProviders.GitHub, settings.GitHub);
                options.AuthorizationEndpoint = "https://github.com/login/oauth/authorize";
                options.TokenEndpoint = "https://github.com/login/oauth/access_token";
                options.UserInformationEndpoint = "https://api.github.com/user";
                options.Scope.Add("read:user");
                options.Scope.Add("user:email");
                options.Events.OnCreatingTicket = ReadGitHubAsync;
            });
        }

        return builder;
    }

    /// <summary>Who the provider said signed in, from the cookie it was kept in; null without an id.</summary>
    public static ExternalIdentity? IdentityOf(AuthenticateResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        var principal = result.Principal;
        var provider = ExternalProviders.Find(principal?.Identity?.AuthenticationType);
        var key = principal?.FindFirstValue(ClaimTypes.NameIdentifier);
        return principal is null || provider is null || string.IsNullOrEmpty(key) || key.Length > ExternalLogin.KeyMaxLength
            ? null
            : new ExternalIdentity(
                provider,
                key,
                principal.FindFirstValue(ClaimTypes.Email),
                principal.FindFirstValue(ClaimTypes.Name),
                principal.FindFirstValue(UsernameClaim));
    }

    private static void Configure(OAuthOptions options, string provider, ExternalProviderOptions settings)
    {
        options.ClientId = settings.ClientId;
        options.ClientSecret = settings.ClientSecret;
        options.CallbackPath = CallbackPath(provider);
        options.SignInScheme = AuthSchemes.External;
        options.UsePkce = true;
        // Named after beeps (CookieNames), and sent back on the provider's top-level redirect like the site's others.
        options.CorrelationCookie.Name = CookieNames.Correlation;
        options.CorrelationCookie.SameSite = SameSiteMode.Lax;
        options.CorrelationCookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
        options.ClaimsIssuer = provider;
        options.Events.OnAccessDenied = context => Back(context, context.Properties, provider);
        options.Events.OnRemoteFailure = context => Back(context, context.Properties, provider);
    }

    /// <summary>
    /// Cancelled at the provider, or its answer did not hold up (expired, replayed, the provider down): back to where it
    /// was started, which says so in the page's language (only known after this, PLAN 6.12).
    /// </summary>
    private static Task Back<TOptions>(HandleRequestContext<TOptions> context, AuthenticationProperties? properties, string provider)
        where TOptions : AuthenticationSchemeOptions
    {
        context.Response.Redirect(FailedPath(provider, properties?.Items.ContainsKey(LinkItem) == true));
        context.HandleResponse();
        return Task.CompletedTask;
    }

    private static async Task ReadGoogleAsync(OAuthCreatingTicketContext context)
    {
        using var user = await GetJsonAsync(context, context.Options.UserInformationEndpoint);
        var root = user.RootElement;
        Add(context, ClaimTypes.NameIdentifier, String(root, "sub"));
        Add(context, ClaimTypes.Name, String(root, "name"));
        // An address Google has not checked could be anyone's.
        if (root.TryGetProperty("email_verified", out var verified) && verified.ValueKind == JsonValueKind.True)
        {
            Add(context, ClaimTypes.Email, String(root, "email"));
        }
    }

    private static async Task ReadGitHubAsync(OAuthCreatingTicketContext context)
    {
        using var user = await GetJsonAsync(context, context.Options.UserInformationEndpoint);
        var root = user.RootElement;
        Add(context, ClaimTypes.NameIdentifier, root.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.Number ? id.GetRawText() : null);
        Add(context, ClaimTypes.Name, String(root, "name"));
        Add(context, UsernameClaim, String(root, "login"));
        // The profile's address may be none, or one never verified: the primary verified one, else any verified one.
        using var emails = await GetJsonAsync(context, "https://api.github.com/user/emails");
        if (emails.RootElement.ValueKind == JsonValueKind.Array)
        {
            var verified = emails.RootElement.EnumerateArray()
                .Where(e => e.TryGetProperty("verified", out var v) && v.ValueKind == JsonValueKind.True)
                .OrderByDescending(e => e.TryGetProperty("primary", out var p) && p.ValueKind == JsonValueKind.True)
                .Select(e => String(e, "email"))
                .FirstOrDefault(e => e is not null);
            Add(context, ClaimTypes.Email, verified);
        }
    }

    private static async Task<JsonDocument> GetJsonAsync(OAuthCreatingTicketContext context, string url)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", context.AccessToken);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        // GitHub's API refuses requests without one.
        request.Headers.UserAgent.Add(new ProductInfoHeaderValue("beeps", "1.0"));
        using var response = await context.Backchannel.SendAsync(request, context.HttpContext.RequestAborted);
        response.EnsureSuccessStatusCode();
        return await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(context.HttpContext.RequestAborted), cancellationToken: context.HttpContext.RequestAborted);
    }

    private static string? String(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String && value.GetString() is { Length: > 0 } text ? text : null;

    private static void Add(OAuthCreatingTicketContext context, string type, string? value)
    {
        if (value is not null)
        {
            context.Identity?.AddClaim(new Claim(type, value, ClaimValueTypes.String, context.Options.ClaimsIssuer));
        }
    }
}

/// <summary>The apps registered with Google and GitHub (<c>OAuth__Google__ClientId</c>…); the secrets never go in the repo.</summary>
public sealed class ExternalLoginOptions
{
    public const string SectionName = "OAuth";

    public ExternalProviderOptions Google { get; set; } = new();

    public ExternalProviderOptions GitHub { get; set; } = new();
}

public sealed class ExternalProviderOptions
{
    public string ClientId { get; set; } = "";

    public string ClientSecret { get; set; } = "";

    public bool IsSet => !string.IsNullOrWhiteSpace(ClientId) && !string.IsNullOrWhiteSpace(ClientSecret);
}
