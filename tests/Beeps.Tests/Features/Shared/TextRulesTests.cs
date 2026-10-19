using Beeps.Features.Shared;

namespace Beeps.Tests.Features.Shared;

public sealed class TextRulesTests
{
    [Theory]
    [InlineData("hello", 10, "hello")]
    [InlineData("hello", 3, "hel")]
    // An emoji outside the BMP is one code point but two UTF-16 units; it is never split.
    [InlineData("ab😀cd", 3, "ab😀")]
    [InlineData("ab😀", 2, "ab")]
    // A ZWJ family is one character of several code points: kept whole or dropped whole.
    [InlineData("a👨‍👩‍👧", 3, "a")]
    public void TruncateCountsCodePointsWithoutSplittingCharacters(string value, int maxLength, string expected) =>
        Assert.Equal(expected, TextRules.Truncate(value, maxLength));
}
