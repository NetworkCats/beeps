using System.Text.Json;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Beeps.Infrastructure.Data;

/// <summary>Stores enums as readable snake_case text (<c>invite_only</c>), not as numbers that silently shift when members are reordered.</summary>
public static class EnumText
{
    public static string ToValue<TEnum>(TEnum value)
        where TEnum : struct, Enum =>
        JsonNamingPolicy.SnakeCaseLower.ConvertName(value.ToString());

    public static TEnum Parse<TEnum>(string value)
        where TEnum : struct, Enum
    {
        foreach (var candidate in Enum.GetValues<TEnum>())
        {
            if (ToValue(candidate) == value)
            {
                return candidate;
            }
        }

        throw new InvalidOperationException($"'{value}' is not a {typeof(TEnum).Name}.");
    }

    public static ValueConverter<TEnum, string> Converter<TEnum>()
        where TEnum : struct, Enum =>
        new(v => ToValue(v), v => Parse<TEnum>(v));
}
