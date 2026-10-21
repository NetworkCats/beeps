namespace Beeps.Features.Site;

/// <summary>A site-wide setting shared by every instance, e.g. the registration mode.</summary>
public sealed class SiteSetting
{
    public const string RegistrationModeKey = "registration_mode";

    public required string Key { get; init; }

    public required string Value { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }
}
