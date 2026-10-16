namespace Beeps.Features.Localization;

public static class SupportedCultures
{
    public const string Default = "en";

    public static readonly IReadOnlyList<string> All = [Default, "zh-Hans"];

    /// <summary>The canonical name of a supported culture, or null.</summary>
    public static string? Find(string? name) =>
        All.FirstOrDefault(c => string.Equals(c, name, StringComparison.OrdinalIgnoreCase));
}
