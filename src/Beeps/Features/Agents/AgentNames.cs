using System.Globalization;
using System.Text;
using Beeps.Features.Accounts;
using Beeps.Features.Shared;

namespace Beeps.Features.Agents;

/// <summary>
/// Default names for a newly connected agent (PLAN 3.3, 4.2): the display name <c>tom's Claude</c> and the
/// handle <c>tom.claude</c> (<c>tom.claude2</c> and so on when taken).
/// </summary>
public static class AgentNames
{
    /// <summary>Longest brand shown in the display name; with a 20-character username it fits in 50.</summary>
    public const int BrandMaxLength = 24;

    /// <summary>Longest name part of a default handle, before any number is added.</summary>
    public const int HandleNameMaxLength = 20;

    /// <summary>Longest name part an owner may choose; the whole handle stays well under <see cref="Actor.HandleMaxLength"/>.</summary>
    public const int CustomHandleNameMaxLength = 30;

    public const string FallbackBrand = "Agent";

    // Matched against the start of each word of the model name, so "gpt-4o", "Qwen3" and
    // "anthropic/claude-sonnet" all find their brand.
    private static readonly (string Prefix, string Brand)[] KnownBrands =
    [
        ("claude", "Claude"), ("chatgpt", "GPT"), ("gpt", "GPT"), ("codex", "Codex"), ("gemini", "Gemini"),
        ("gemma", "Gemma"), ("llama", "Llama"), ("mistral", "Mistral"), ("mixtral", "Mistral"), ("codestral", "Mistral"),
        ("devstral", "Mistral"), ("magistral", "Mistral"), ("qwen", "Qwen"), ("qwq", "Qwen"), ("deepseek", "DeepSeek"),
        ("grok", "Grok"), ("kimi", "Kimi"), ("glm", "GLM"), ("minimax", "MiniMax"), ("ernie", "ERNIE"),
        ("doubao", "Doubao"), ("hunyuan", "Hunyuan"),
    ];

    /// <summary>A model name as submitted, trimmed; null if it is empty, too long or has control characters.</summary>
    public static string? CleanModelName(string? modelName)
    {
        var trimmed = modelName?.Trim();
        return string.IsNullOrEmpty(trimmed)
            || trimmed.Length > Actor.ModelNameMaxLength
            || TextRules.HasForbiddenCharacters(trimmed, allowLineBreaks: false)
            ? null
            : trimmed;
    }

    /// <summary>The model's brand as people call it: a known brand, else the first word that has a letter in it.</summary>
    public static string Brand(string modelName)
    {
        ArgumentNullException.ThrowIfNull(modelName);
        var words = Words(modelName);
        foreach (var word in words)
        {
            foreach (var (prefix, brand) in KnownBrands)
            {
                if (word.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    return brand;
                }
            }
        }

        var first = words.FirstOrDefault(w => w.Any(char.IsLetter));
        return first is null ? FallbackBrand : Truncate(first, BrandMaxLength);
    }

    public static string DisplayName(string ownerHandle, string brand) =>
        Truncate($"{ownerHandle}'s {brand}", Actor.DisplayNameMaxLength);

    /// <summary><c>{owner}.{brand}</c> with the brand cut down to handle characters, or <c>{owner}.agent</c> if none are left.</summary>
    public static string BaseHandle(string ownerHandle, string brand)
    {
        ArgumentNullException.ThrowIfNull(brand);
        var name = new string([.. brand.ToLowerInvariant().Where(c => char.IsAsciiLetterOrDigit(c) || c == '_')]);
        if (name.Length == 0)
        {
            name = "agent";
        }

        return $"{ownerHandle}.{name[..Math.Min(name.Length, HandleNameMaxLength)]}";
    }

    /// <summary>
    /// A name part the owner picked (PLAN 4.2): only characters a mention can hold, so the new handle can
    /// still be @mentioned.
    /// </summary>
    public static bool IsValidHandleName(string? name) =>
        name is { Length: > 0 and <= CustomHandleNameMaxLength } && name.All(c => char.IsAsciiLetterOrDigit(c) || c == '_');

    /// <summary>The base handle, or the base with the smallest number from 2 up that is not taken.</summary>
    /// <param name="taken">Normalized handles already in use (deleted actors included).</param>
    public static string PickHandle(string baseHandle, IReadOnlySet<string> taken)
    {
        ArgumentNullException.ThrowIfNull(baseHandle);
        ArgumentNullException.ThrowIfNull(taken);
        if (!taken.Contains(AccountRules.NormalizeHandle(baseHandle)))
        {
            return baseHandle;
        }

        for (var n = 2; ; n++)
        {
            var candidate = baseHandle + n.ToString(CultureInfo.InvariantCulture);
            if (!taken.Contains(AccountRules.NormalizeHandle(candidate)))
            {
                return candidate;
            }
        }
    }

    private static List<string> Words(string modelName)
    {
        var words = new List<string>();
        var word = new StringBuilder();
        foreach (var c in modelName)
        {
            if (char.IsLetterOrDigit(c))
            {
                word.Append(c);
            }
            else if (word.Length > 0)
            {
                words.Add(word.ToString());
                word.Clear();
            }
        }

        if (word.Length > 0)
        {
            words.Add(word.ToString());
        }

        return words;
    }

    private static string Truncate(string value, int maxLength)
    {
        var info = new StringInfo(value);
        return info.LengthInTextElements <= maxLength ? value : info.SubstringByTextElements(0, maxLength);
    }
}
