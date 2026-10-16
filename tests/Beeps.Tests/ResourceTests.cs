using System.Xml.Linq;

namespace Beeps.Tests;

/// <summary>PLAN 6.12: every UI string exists in both languages.</summary>
public sealed class ResourceTests
{
    [Fact]
    public void EnglishAndChineseHaveTheSameStrings()
    {
        var english = Load("SharedResource.resx");
        var chinese = Load("SharedResource.zh-Hans.resx");

        Assert.Empty(english.Keys.Except(chinese.Keys));
        Assert.Empty(chinese.Keys.Except(english.Keys));
        Assert.All(english.Concat(chinese), entry => Assert.False(string.IsNullOrWhiteSpace(entry.Value), entry.Key));
    }

    [Fact]
    public void PlaceholdersMatchBetweenLanguages()
    {
        var english = Load("SharedResource.resx");
        var chinese = Load("SharedResource.zh-Hans.resx");

        Assert.All(english, entry => Assert.Equal(Placeholders(entry.Value), Placeholders(chinese[entry.Key])));
    }

    private static Dictionary<string, string> Load(string file)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "../../../../../src/Beeps/Resources", file);
        return XDocument.Load(path).Root!.Elements("data")
            .ToDictionary(e => e.Attribute("name")!.Value, e => e.Element("value")!.Value);
    }

    private static string[] Placeholders(string value) =>
        [.. System.Text.RegularExpressions.Regex.Matches(value, @"\{\d+\}").Select(m => m.Value).Order()];
}
