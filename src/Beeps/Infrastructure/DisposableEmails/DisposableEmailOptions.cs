using System.ComponentModel.DataAnnotations;

namespace Beeps.Infrastructure.DisposableEmails;

public sealed class DisposableEmailOptions
{
    public const string SectionName = "DisposableEmail";

    /// <summary>Whether this instance fetches the list. Off, sign-up takes any address.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>One domain a line, kept up to date upstream.</summary>
    [Required]
    public Uri ListUrl { get; set; } = new("https://raw.githubusercontent.com/disposable-email-domains/disposable-email-domains/main/disposable_email_blocklist.conf");

    /// <summary>A copy is fetched again once it is this old.</summary>
    public TimeSpan RefreshInterval { get; set; } = TimeSpan.FromDays(7);

    /// <summary>After a failed fetch, the next try; the copy already loaded stays in use meanwhile.</summary>
    public TimeSpan RetryInterval { get; set; } = TimeSpan.FromHours(1);

    public TimeSpan DownloadTimeout { get; set; } = TimeSpan.FromMinutes(1);

    /// <summary>A larger download is refused (the list is about 150 KB).</summary>
    [Range(1, long.MaxValue)]
    public long MaxDownloadBytes { get; set; } = 16L * 1024 * 1024;

    /// <summary>Where the copy is kept between restarts; empty for a folder in the system's temp directory.</summary>
    public string? Directory { get; set; }

    public string ResolvedDirectory => string.IsNullOrWhiteSpace(Directory) ? Path.Combine(Path.GetTempPath(), "beeps-disposable-email") : Directory;
}
