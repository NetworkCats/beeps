using System.ComponentModel.DataAnnotations;

namespace Beeps.Features.Accounts;

/// <summary>Server-side check of a human-verification token (Cloudflare Turnstile in production).</summary>
public interface ICaptchaVerifier
{
    /// <returns>False for a missing, invalid, expired or reused token, and when the check itself fails.</returns>
    Task<bool> VerifyAsync(string? token, string clientIp, CancellationToken cancellationToken);
}

public sealed class CaptchaOptions
{
    public const string SectionName = "Turnstile";

    /// <summary>Public; rendered into the page for the widget.</summary>
    [Required]
    public string SiteKey { get; set; } = "";

    [Required]
    public string SecretKey { get; set; } = "";
}
