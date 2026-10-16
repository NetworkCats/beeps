using Beeps.Features.Profiles;

namespace Beeps.Tests.Features.Profiles;

public sealed class ProfileRulesTests
{
    [Theory]
    [InlineData("  Tom  ", "Tom")]
    [InlineData("汤姆 🤖", "汤姆 🤖")]
    [InlineData("", null)]
    [InlineData("   ", null)]
    [InlineData(null, null)]
    [InlineData("tab\there", null)]
    [InlineData("two\nlines", null)]
    public void DisplayNamesAreTrimmedSingleLines(string? input, string? expected) =>
        Assert.Equal(expected, ProfileRules.CleanDisplayName(input));

    [Fact]
    public void DisplayNameLengthCountsCodePointsLikePostgres()
    {
        // 50 emoji are 100 UTF-16 units but 50 characters in a varchar(50) column.
        Assert.NotNull(ProfileRules.CleanDisplayName(string.Concat(Enumerable.Repeat("🤖", 50))));
        Assert.Null(ProfileRules.CleanDisplayName(new string('a', 51)));
        Assert.Null(ProfileRules.CleanDisplayName("\uD83E"));
    }

    [Fact]
    public void AnEmptyBioIsNoBio()
    {
        Assert.True(ProfileRules.TryCleanBio("  \r\n ", out var cleaned));
        Assert.Null(cleaned);
        Assert.True(ProfileRules.TryCleanBio(null, out cleaned));
        Assert.Null(cleaned);
    }

    [Fact]
    public void BiosKeepLineBreaksInOneForm()
    {
        Assert.True(ProfileRules.TryCleanBio(" first\r\nsecond ", out var cleaned));
        Assert.Equal("first\nsecond", cleaned);
    }

    [Fact]
    public void BiosHaveALimitAndNoControlCharacters()
    {
        Assert.True(ProfileRules.TryCleanBio(new string('a', 300), out _));
        Assert.False(ProfileRules.TryCleanBio(new string('a', 301), out _));
        Assert.False(ProfileRules.TryCleanBio("bell\a", out _));
    }
}
