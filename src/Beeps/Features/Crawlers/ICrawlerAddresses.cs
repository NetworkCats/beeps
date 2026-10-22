using System.Net;

namespace Beeps.Features.Crawlers;

/// <summary>The addresses Google's and Bing's crawlers and fetchers come from, as their owners publish them.</summary>
public interface ICrawlerAddresses
{
    /// <summary>Whether a copy of the lists is loaded; until then no address is known.</summary>
    bool IsLoaded { get; }

    bool Contains(IPAddress address);
}

public static class CrawlerRequests
{
    /// <summary>Whether the request comes from Google's or Bing's crawlers; never while the lists are not loaded yet.</summary>
    public static bool FromCrawler(this HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return context.Connection.RemoteIpAddress is { } address && context.RequestServices.GetRequiredService<ICrawlerAddresses>().Contains(address);
    }
}
