using System.Net;
using Beeps.Features.Crawlers;

namespace Beeps.Infrastructure.Crawlers;

/// <summary>Answers from the copy of the lists <see cref="CrawlerAddressUpdater"/> loaded last; a newer one replaces it whole.</summary>
public sealed class CrawlerAddressList : ICrawlerAddresses
{
    private IReadOnlyList<IPNetwork>? networks;

    public bool IsLoaded => Volatile.Read(ref networks) is not null;

    /// <summary>Starts answering from <paramref name="next"/>, never changed after.</summary>
    public void Use(IReadOnlyList<IPNetwork> next)
    {
        ArgumentNullException.ThrowIfNull(next);
        Volatile.Write(ref networks, next);
    }

    public bool Contains(IPAddress address)
    {
        ArgumentNullException.ThrowIfNull(address);
        if (Volatile.Read(ref networks) is not { } current)
        {
            return false;
        }

        // Kestrel listens on [::], so an IPv4 visitor can arrive as ::ffff:66.249.66.1.
        if (address.IsIPv4MappedToIPv6)
        {
            address = address.MapToIPv4();
        }

        // About two thousand ranges, and asked only by the sitemap: a scan is quick enough.
        foreach (var network in current)
        {
            if (network.Contains(address))
            {
                return true;
            }
        }

        return false;
    }
}
