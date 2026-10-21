using System.Globalization;
using Beeps.Infrastructure.Previews;

namespace Beeps.Tests.Infrastructure.Previews;

public sealed class TextLayoutTests
{
    [Fact]
    public void LatinTextWrapsAtSpacesWhichDoNotStartOrEndALine()
    {
        var lines = Wrap("the quick brown fox jumps", width: 100);

        Assert.Equal(["the quick", "brown fox", "jumps"], lines);
    }

    [Fact]
    public void ChineseWrapsBetweenAnyCharactersButNotBeforeClosingPunctuation()
    {
        // Five fit on a line, but "，" may not start the next: "你" goes down with it.
        var lines = Wrap("我们一起你，好的", width: 50);

        Assert.Equal(["我们一起", "你，好的"], lines);
    }

    [Fact]
    public void NoLineEndsWithOpeningPunctuation()
    {
        var lines = Wrap("一二三四（五六）", width: 50);

        Assert.Equal(["一二三四", "（五六）"], lines);
    }

    [Fact]
    public void AWordLongerThanALineIsBrokenWhereItNoLongerFits()
    {
        var lines = Wrap("abcdefghijklmno", width: 50);

        Assert.Equal(["abcde", "fghij", "klmno"], lines);
    }

    [Fact]
    public void ALinkBreaksAfterASlash()
    {
        var lines = Wrap("see example.com/a/path", width: 120);

        Assert.Equal(["see", "example.com/", "a/path"], lines);
    }

    [Fact]
    public void LineBreaksInTheTextAreKeptAndBlankLinesWithThem()
    {
        var lines = Wrap("one\n\ntwo", width: 100);

        Assert.Equal(["one", "", "two"], lines);
    }

    [Theory]
    [InlineData("👍", true)]
    [InlineData("☀️", true)]
    [InlineData("👨‍💻", true)]
    [InlineData("1️⃣", true)]
    [InlineData("🇨🇳", true)]
    [InlineData("©", false)]
    [InlineData("a", false)]
    [InlineData("中", false)]
    public void EmojiAreToldApartFromText(string textElement, bool emoji) =>
        Assert.Equal(emoji, PreviewFonts.IsEmoji(textElement));

    /// <summary>Every character 10 wide (five to a line of 50), spaces 5.</summary>
    private static string[] Wrap(string text, float width)
    {
        var elements = new List<string>();
        var enumerator = StringInfo.GetTextElementEnumerator(text);
        while (enumerator.MoveNext())
        {
            elements.Add(enumerator.GetTextElement());
        }

        return [.. TextLayout.Wrap(elements, e => e == " " ? 5 : 10, width)];
    }
}
