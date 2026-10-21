using System.ComponentModel.DataAnnotations;

namespace Beeps.Infrastructure.Crawlers;

public sealed class CrawlerAddressOptions
{
    public const string SectionName = "Crawlers";

    /// <summary>Whether this instance fetches the addresses. Off, the sitemap is served to no one.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>A copy is fetched again once it is this old; Google updates its lists daily.</summary>
    public TimeSpan RefreshInterval { get; set; } = TimeSpan.FromDays(1);

    /// <summary>After a failed fetch, the next try; the copy already loaded stays in use meanwhile.</summary>
    public TimeSpan RetryInterval { get; set; } = TimeSpan.FromHours(1);

    public TimeSpan DownloadTimeout { get; set; } = TimeSpan.FromMinutes(1);

    /// <summary>A larger list is refused (the largest is about 75 KB).</summary>
    [Range(1, long.MaxValue)]
    public long MaxDownloadBytes { get; set; } = 4L * 1024 * 1024;

    /// <summary>Where the copy is kept between restarts; empty for a folder in the system's temp directory.</summary>
    public string? Directory { get; set; }

    public string ResolvedDirectory => string.IsNullOrWhiteSpace(Directory) ? Path.Combine(Path.GetTempPath(), "beeps-crawlers") : Directory;
}
