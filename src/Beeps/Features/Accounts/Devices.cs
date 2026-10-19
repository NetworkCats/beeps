namespace Beeps.Features.Accounts;

/// <summary>What sort of device a session was signed in on, which picks its icon on the devices page.</summary>
public enum DeviceKind
{
    Unknown,
    Phone,
    Tablet,
    Computer,
}

/// <summary>A device as its User-Agent names it.</summary>
/// <param name="Name">The device or system ("iPhone", "Mac", "Android"); null when the User-Agent does not say.</param>
/// <param name="Browser">The browser or app it was signed in with; null when the User-Agent does not say.</param>
public sealed record DeviceInfo(DeviceKind Kind, string? Name, string? Browser)
{
    public static readonly DeviceInfo Unknown = new(DeviceKind.Unknown, null, null);
}

/// <summary>
/// Names the device behind a User-Agent, as sign-in lists elsewhere do (PLAN 6.14). Only what the owner needs to tell
/// their devices apart: the brand names are the same in every language, and no versions, which change on every update.
/// </summary>
public static class UserAgents
{
    // Checked in order: in-app browsers and Chromium's offshoots also say "Chrome" and "Safari", and Chrome says "Safari".
    private static readonly (string Token, string Name)[] Browsers =
    [
        ("MicroMessenger/", "WeChat"),
        ("EdgA/", "Edge"),
        ("EdgiOS/", "Edge"),
        ("Edg/", "Edge"),
        ("Edge/", "Edge"),
        ("OPR/", "Opera"),
        ("OPiOS/", "Opera"),
        ("SamsungBrowser/", "Samsung Internet"),
        ("QQBrowser/", "QQ Browser"),
        ("UCBrowser/", "UC Browser"),
        ("YaBrowser/", "Yandex Browser"),
        ("Vivaldi/", "Vivaldi"),
        ("FxiOS/", "Firefox"),
        ("Firefox/", "Firefox"),
        ("CriOS/", "Chrome"),
        ("Chrome/", "Chrome"),
        ("Safari/", "Safari"),
    ];

    public static DeviceInfo Describe(string? userAgent)
    {
        if (string.IsNullOrWhiteSpace(userAgent))
        {
            return DeviceInfo.Unknown;
        }

        var (kind, name) = Device(userAgent);
        var browser = Browsers.FirstOrDefault(b => userAgent.Contains(b.Token, StringComparison.Ordinal)).Name;
        return new DeviceInfo(kind, name, browser);
    }

    private static (DeviceKind Kind, string? Name) Device(string ua)
    {
        bool Has(string token) => ua.Contains(token, StringComparison.Ordinal);

        if (Has("iPhone"))
        {
            return (DeviceKind.Phone, "iPhone");
        }

        if (Has("iPad"))
        {
            return (DeviceKind.Tablet, "iPad");
        }

        if (Has("iPod"))
        {
            return (DeviceKind.Phone, "iPod touch");
        }

        // HarmonyOS NEXT names the form factor first: "(Phone; OpenHarmony 5.0)", "(Tablet; …)", "(PC; …)".
        if (Has("OpenHarmony") || Has("HarmonyOS"))
        {
            return (Has("Tablet;") ? DeviceKind.Tablet : Has("PC;") ? DeviceKind.Computer : DeviceKind.Phone, "HarmonyOS");
        }

        // Android browsers say "Mobile" on phones and leave it out on tablets.
        if (Has("Android"))
        {
            return (Has("Mobile") ? DeviceKind.Phone : DeviceKind.Tablet, "Android");
        }

        if (Has("CrOS"))
        {
            return (DeviceKind.Computer, "ChromeOS");
        }

        if (Has("Windows Phone"))
        {
            return (DeviceKind.Phone, "Windows Phone");
        }

        if (Has("Windows"))
        {
            return (DeviceKind.Computer, "Windows");
        }

        // An iPad on iPadOS 13 or later says it is a Mac, so it is listed as one: the User-Agent cannot tell them apart.
        if (Has("Macintosh") || Has("Mac OS X"))
        {
            return (DeviceKind.Computer, "Mac");
        }

        if (Has("Linux") || Has("X11"))
        {
            return (DeviceKind.Computer, "Linux");
        }

        return (DeviceKind.Unknown, null);
    }
}
