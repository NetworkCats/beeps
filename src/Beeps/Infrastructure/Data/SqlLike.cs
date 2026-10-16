namespace Beeps.Infrastructure.Data;

/// <summary>Prefix matching with <c>LIKE</c>: user input is escaped so <c>%</c> and <c>_</c> match themselves.</summary>
internal static class SqlLike
{
    public const string Escape = "\\";

    public static string Prefix(string prefix)
    {
        ArgumentNullException.ThrowIfNull(prefix);
        return prefix.Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("%", "\\%", StringComparison.Ordinal)
            .Replace("_", "\\_", StringComparison.Ordinal) + "%";
    }
}
