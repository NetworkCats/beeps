using System.Net;

namespace Beeps.Features.Crawlers;

/// <summary>The addresses Google's and Bing's crawlers and fetchers come from, as their owners publish them.</summary>
public interface ICrawlerAddresses
{
    /// <summary>Whether a copy of the lists is loaded; until then no address is known.</summary>
    bool IsLoaded { get; }

    bool Contains(IPAddress address);
}
