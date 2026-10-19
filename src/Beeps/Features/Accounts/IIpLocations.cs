using System.Globalization;

namespace Beeps.Features.Accounts;

/// <summary>
/// Where an IP address is, roughly, from an IP geolocation database each instance keeps a copy of and refreshes daily
/// (<c>Infrastructure/IpLocation</c>). Answers from memory; null while no copy is loaded, and for addresses it does not know
/// (private and loopback ones among them).
/// </summary>
public interface IIpLocations
{
    /// <param name="culture">The language to name the places in, where the database has it.</param>
    IpLocation? Find(string? ipAddress, CultureInfo culture);
}

/// <summary>Place names, each in the language asked for if the database has it, else in another.</summary>
public sealed record IpLocation(string? City, string? Region, string? Country);
