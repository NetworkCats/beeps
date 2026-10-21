using System.Globalization;
using System.Net;
using Beeps.Features.Accounts;
using Beeps.Features.Shared;
using MaxMind.Db;

namespace Beeps.Infrastructure.IpGeolocation;

/// <summary>
/// Looks addresses up in the copy of the IP database <see cref="IpLocationUpdater"/> loaded last. Lookups read the
/// memory-mapped file with no lock; a copy replaced by a newer one is closed a little later, once lookups still
/// reading it are long done.
/// </summary>
public sealed partial class MaxMindIpLocations(TimeProvider time, ILogger<MaxMindIpLocations> logger) : IIpLocations, IDisposable
{
    /// <summary>How long a replaced copy stays open: far longer than any lookup takes.</summary>
    internal static readonly TimeSpan RetireDelay = TimeSpan.FromMinutes(1);

    private readonly LogThrottle lookupFailures = new();

    private Reader? reader;

    /// <summary>Whether a copy is loaded.</summary>
    public bool IsLoaded => Volatile.Read(ref reader) is not null;

    /// <summary>Starts answering from <paramref name="next"/>, which this then owns.</summary>
    public void Use(Reader next)
    {
        ArgumentNullException.ThrowIfNull(next);
        if (Interlocked.Exchange(ref reader, next) is { } replaced)
        {
            _ = RetireAsync(replaced);
        }
    }

    public IpLocation? Find(string? ipAddress, CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(culture);
        var current = Volatile.Read(ref reader);
        if (current is null || !IPAddress.TryParse(ipAddress, out var address))
        {
            return null;
        }

        // Kestrel reports IPv4 clients on a dual-stack socket as ::ffff:a.b.c.d; the database files them as IPv4.
        if (address.IsIPv4MappedToIPv6)
        {
            address = address.MapToIPv4();
        }

        IpRecord? record;
        try
        {
            record = current.Find<IpRecord>(address);
        }
        catch (ObjectDisposedException)
        {
            // Replaced and closed mid-lookup, which the retire delay all but rules out: no place this once.
            return null;
        }
        catch (Exception exception) when (exception is InvalidDatabaseException or ArgumentException)
        {
            // An IPv6 address in an IPv4-only database, or a damaged record.
            if (lookupFailures.Allow())
            {
                LogLookupFailed(logger, exception);
            }
            return null;
        }

        if (record is null)
        {
            return null;
        }

        var languages = LanguagesFor(culture);
        var location = new IpLocation(
            Name(record.City, languages),
            Name(record.Subdivisions?.FirstOrDefault(), languages),
            Name(record.Country, languages) ?? CountryFromCode(record.Country?.IsoCode));
        return location is { City: null, Region: null, Country: null } ? null : location;
    }

    public void Dispose() => Interlocked.Exchange(ref reader, null)?.Dispose();

    /// <summary>The database's name keys to try, best first: the page language, then English, which nearly every place has.</summary>
    internal static string[] LanguagesFor(CultureInfo culture) => culture.TwoLetterISOLanguageName switch
    {
        "zh" => ["zh-CN", "en"],
        "pt" => ["pt-BR", "en"],
        var language => [language, "en"],
    };

    private static string? Name(IpPlace? place, string[] languages)
    {
        if (place?.Names is not { Count: > 0 } names)
        {
            return null;
        }

        foreach (var language in languages)
        {
            if (names.TryGetValue(language, out var name) && !string.IsNullOrWhiteSpace(name))
            {
                return name;
            }
        }

        // Some places are named in one language only (Chinese towns from QQWry, in zh-CN).
        return names.Values.FirstOrDefault(n => !string.IsNullOrWhiteSpace(n));
    }

    /// <summary>Some networks carry only the country's code.</summary>
    private static string? CountryFromCode(string? isoCode)
    {
        if (isoCode is not { Length: 2 })
        {
            return null;
        }

        try
        {
            return new RegionInfo(isoCode).EnglishName;
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    private async Task RetireAsync(Reader replaced)
    {
        try
        {
            await Task.Delay(RetireDelay, time);
        }
        finally
        {
            replaced.Dispose();
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Looking up an IP address failed (logged at most once a minute)")]
    private static partial void LogLookupFailed(ILogger logger, Exception exception);
}

/// <summary>The parts of a record of the database that name a place (its README lists the rest).</summary>
internal sealed class IpRecord
{
    [Constructor]
    public IpRecord(
        [MapKey("city")] IpPlace? city,
        [MapKey("subdivisions")] List<IpPlace>? subdivisions,
        [MapKey("country")] IpPlace? country)
    {
        City = city;
        Subdivisions = subdivisions;
        Country = country;
    }

    public IpPlace? City { get; }

    public List<IpPlace>? Subdivisions { get; }

    public IpPlace? Country { get; }
}

internal sealed class IpPlace
{
    [Constructor]
    public IpPlace([MapKey("names")] Dictionary<string, string>? names, [MapKey("iso_code")] string? isoCode)
    {
        Names = names;
        IsoCode = isoCode;
    }

    public Dictionary<string, string>? Names { get; }

    public string? IsoCode { get; }
}
