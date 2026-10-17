using System.Security.Claims;
using Beeps.Features.Auth;

namespace Beeps.Features.Site;

/// <summary>
/// The colour scheme picked by hand: <see cref="Light"/>, <see cref="Dark"/>, or <see cref="System"/> to follow the
/// device. A signed-in account keeps its choice (every device, every tab); otherwise the browser does, in
/// <see cref="ThemeCookie"/>. The layout writes the scheme as <c>&lt;html data-theme&gt;</c>, which collection-ui turns
/// into the page's <c>color-scheme</c>.
/// </summary>
public static class Themes
{
    public const string System = "system";

    public const string Light = "light";

    public const string Dark = "dark";

    public static readonly IReadOnlyList<string> All = [System, Light, Dark];

    public const int MaxLength = 16;

    /// <summary>One of <see cref="All"/>, or null.</summary>
    public static string? Find(string? theme) => theme is System or Light or Dark ? theme : null;

    /// <summary>
    /// What this request was made with, one of <see cref="All"/>: the account's choice, or (signed out, or never
    /// chosen while signed in) the browser's.
    /// </summary>
    public static string For(ClaimsPrincipal user, HttpRequest request)
    {
        ArgumentNullException.ThrowIfNull(user);
        ArgumentNullException.ThrowIfNull(request);
        return Find(user.IsHuman() ? user.FindFirstValue(BeepsClaims.Theme) : null) ?? ThemeCookie.Read(request) ?? System;
    }

    /// <summary>The scheme to draw the page in: <see cref="Light"/> or <see cref="Dark"/>, or null for the device's.</summary>
    public static string? Scheme(string theme) => theme is Light or Dark ? theme : null;
}
