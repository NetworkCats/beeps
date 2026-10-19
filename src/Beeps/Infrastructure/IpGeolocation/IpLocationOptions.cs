using System.ComponentModel.DataAnnotations;

namespace Beeps.Infrastructure.IpGeolocation;

public sealed class IpLocationOptions
{
    public const string SectionName = "IpLocation";

    /// <summary>Whether this instance fetches the database. Off, the devices page shows no places.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>The MMDB file, rebuilt upstream every day; the address always leads to the latest release.</summary>
    [Required]
    public Uri DatabaseUrl { get; set; } = new("https://github.com/NetworkCats/Merged-IP-Data/releases/latest/download/Merged-IP.mmdb");

    /// <summary>A copy is fetched again once it is this old.</summary>
    public TimeSpan RefreshInterval { get; set; } = TimeSpan.FromDays(1);

    /// <summary>After a failed fetch, the next try; the copy already loaded stays in use meanwhile.</summary>
    public TimeSpan RetryInterval { get; set; } = TimeSpan.FromHours(1);

    public TimeSpan DownloadTimeout { get; set; } = TimeSpan.FromMinutes(10);

    /// <summary>A larger download is refused, so a wrong address cannot fill the disk (the file is about 100 MB).</summary>
    [Range(1, long.MaxValue)]
    public long MaxDownloadBytes { get; set; } = 512L * 1024 * 1024;

    /// <summary>Where the copy is kept between restarts; empty for a folder in the system's temp directory.</summary>
    public string? Directory { get; set; }

    public string ResolvedDirectory => string.IsNullOrWhiteSpace(Directory) ? Path.Combine(Path.GetTempPath(), "beeps-ip-location") : Directory;
}
