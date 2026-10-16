using System.ComponentModel.DataAnnotations;

namespace Beeps.Infrastructure.Storage;

/// <summary>
/// The S3-compatible bucket media is stored in (PLAN 6.11, 9.1): Cloudflare R2 in production, SeaweedFS
/// locally. Only standard S3 calls are made, so the two differ in configuration alone.
/// </summary>
public sealed class StorageOptions
{
    public const string SectionName = "Storage";

    /// <summary>The S3 endpoint, e.g. <c>https://&lt;account&gt;.r2.cloudflarestorage.com</c> or <c>http://localhost:8333</c>.</summary>
    [Required]
    [Url]
    public string ServiceUrl { get; set; } = "";

    /// <summary><c>auto</c> for R2.</summary>
    [Required]
    public string Region { get; set; } = "auto";

    [Required]
    public string Bucket { get; set; } = "";

    [Required]
    public string AccessKeyId { get; set; } = "";

    [Required]
    public string SecretAccessKey { get; set; } = "";
}
