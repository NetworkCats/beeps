using Beeps.Features.Accounts;
using Beeps.Features.Beeps;
using Beeps.Features.Shared;
using Beeps.Tests.Support;

namespace Beeps.Tests.Features.Beeps;

public sealed class BeepTextTests
{
    [Theory]
    [InlineData("#beeps", new[] { "beeps" })]
    [InlineData("Shipped it #Beeps!", new[] { "beeps" })]
    [InlineData("#天气 今天很好", new[] { "天气" })]
    [InlineData("#天气今天很好", new[] { "天气今天很好" })]
    [InlineData("(#a1) #2026年", new[] { "a1", "2026年" })]
    [InlineData("#ＢＥＥＰＳ and #beeps", new[] { "beeps" })]
    [InlineData("#snake_case_tag.", new[] { "snake_case_tag" })]
    public void HashtagsEndAtSpacesAndPunctuation(string text, string[] expected) =>
        Assert.Equal(expected, BeepText.Hashtags(text));

    [Theory]
    [InlineData("page#2")]
    [InlineData("今天#天气")]
    [InlineData("#123")]
    [InlineData("#_")]
    [InlineData("example.com/#top")]
    [InlineData("&#39; quoted")]
    [InlineData("## heading")]
    public void SomeHashSignsAreNotHashtags(string text) =>
        Assert.Empty(BeepText.Hashtags(text));

    [Theory]
    [InlineData("hi @tom", "tom")]
    [InlineData("@tom.claude2 look", "tom.claude2")]
    [InlineData("thanks @tom.", "tom")]
    [InlineData("@tom你好", "tom")]
    [InlineData("（@tom）", "tom")]
    public void MentionsAreUsernamesOrAgentHandles(string text, string handle)
    {
        var mention = Assert.Single(BeepText.Tokenize(text), t => t.Kind == BeepTextTokenKind.Mention);
        Assert.Equal(handle, mention.Value);
    }

    [Theory]
    [InlineData("mail me at tom@example.com")]
    [InlineData("@ nobody")]
    public void EmailAddressesAreNotMentions(string text) =>
        Assert.DoesNotContain(BeepText.Tokenize(text), t => t.Kind == BeepTextTokenKind.Mention);

    [Fact]
    public void ParsingNormalizesTheText()
    {
        var parsed = BeepText.Parse("  hello\r\n@Tom @tom #A #a  ");

        Assert.True(parsed.IsValid);
        Assert.Equal("hello\n@Tom @tom #A #a", parsed.Text);
        Assert.Equal(["a"], parsed.Hashtags);
        Assert.Equal(["tom"], parsed.MentionHandles);
    }

    [Fact]
    public void LengthCountsWhatPeopleSee()
    {
        // A family emoji is 11 UTF-16 code units but one character.
        Assert.True(BeepText.Parse(string.Concat(Enumerable.Repeat("👨‍👩‍👧", 500))).IsValid);
        Assert.True(BeepText.Parse(new string('字', 500)).IsValid);
        Assert.Equal(BeepTextProblem.TooLong, BeepText.Parse(new string('字', 501)).Problem);
    }

    [Theory]
    [InlineData(null, BeepTextProblem.Empty)]
    [InlineData(" \n ", BeepTextProblem.Empty)]
    [InlineData("bell\u0007", BeepTextProblem.ForbiddenCharacters)]
    [InlineData("#a1 #a2 #a3 #a4 #a5 #a6 #a7 #a8 #a9 #a10 #a11", BeepTextProblem.TooManyHashtags)]
    [InlineData("@a1 @a2 @a3 @a4 @a5 @a6 @a7 @a8 @a9 @a10 @a11", BeepTextProblem.TooManyMentions)]
    public void BadTextIsRejected(string? text, BeepTextProblem problem) =>
        Assert.Equal(problem, BeepText.Parse(text).Problem);

    // Built at run time: xUnit would turn a lone surrogate in InlineData into U+FFFD.
    [Fact]
    public void LoneSurrogatesCannotBeStored()
    {
        var high = "lone " + (char)0xD800 + " surrogate";
        var low = (char)0xDC00 + " lone low surrogate";

        Assert.Equal(BeepTextProblem.ForbiddenCharacters, BeepText.Parse(high).Problem);
        Assert.True(TextRules.HasForbiddenCharacters(low, allowLineBreaks: true));
        Assert.False(TextRules.HasForbiddenCharacters("pair 😀", allowLineBreaks: false));
    }

    [Fact]
    public void RepeatedTagsAndMentionsCountOnce() =>
        Assert.True(BeepText.Parse(string.Join(' ', Enumerable.Repeat("#same @same", 20))).IsValid);

    [Fact]
    public void HashtagsHaveAMaximumLength()
    {
        Assert.True(BeepText.Parse("#" + new string('a', 50)).IsValid);
        Assert.Equal(BeepTextProblem.HashtagTooLong, BeepText.Parse("#" + new string('a', 51)).Problem);
    }

    [Theory]
    [InlineData("beeps", "beeps")]
    [InlineData("#Beeps", "beeps")]
    [InlineData(" 天气 ", "天气")]
    public void AHashtagOnItsOwnIsNormalized(string input, string expected)
    {
        Assert.True(BeepText.TryNormalizeHashtag(input, out var normalized));
        Assert.Equal(expected, normalized);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("#")]
    [InlineData("two words")]
    [InlineData("123")]
    [InlineData("tag!")]
    public void NotAHashtagOnItsOwn(string? input) =>
        Assert.False(BeepText.TryNormalizeHashtag(input, out _));

    [Fact]
    public void RenderingEscapesFirstThenLinks()
    {
        var html = BeepText.RenderHtml("<b>hi</b> & #Beeps @tom @ghost\nbye", new HashSet<string> { "tom" });

        Assert.Equal(
            "&lt;b&gt;hi&lt;/b&gt; &amp; <a class=\"hashtag\" href=\"/tags/beeps\">#Beeps</a> " +
            "<a class=\"mention\" href=\"/@tom\">@tom</a> @ghost<br>bye",
            html);
    }

    [Fact]
    public void ABeepRendersWithLinksForTheMentionsThatResolved()
    {
        var tom = new ActorView(Guid.NewGuid(), ActorKind.Human, "Tom", "Tom", new Uri("https://beeps.test/a.svg"), Relations.Other);
        var beep = new BeepView(Guid.NewGuid(), "@tom @ghost #x", tom, null, Guid.NewGuid(), TestData.Now, 0, 0, false, [tom], ["x"], [], false);

        Assert.Equal(
            "<a class=\"mention\" href=\"/@tom\">@tom</a> @ghost <a class=\"hashtag\" href=\"/tags/x\">#x</a>",
            BeepText.RenderHtml(beep));
    }

    [Fact]
    public void RenderedTagLinksAreEncoded()
    {
        var html = BeepText.RenderHtml("#天气", new HashSet<string>());

        Assert.Equal("<a class=\"hashtag\" href=\"/tags/%E5%A4%A9%E6%B0%94\">#天气</a>", html);
    }

    [Theory]
    [InlineData("see https://example.com", "https://example.com")]
    [InlineData("HTTP://Example.COM/a?b=1&c=%20#top", "HTTP://Example.COM/a?b=1&c=%20#top")]
    [InlineData("see https://example.com/a.", "https://example.com/a")]
    [InlineData("(see https://example.com/a)", "https://example.com/a")]
    [InlineData("https://en.wikipedia.org/wiki/Beep_(sound))", "https://en.wikipedia.org/wiki/Beep_(sound)")]
    [InlineData("看看https://example.com/a这个", "https://example.com/a")]
    [InlineData("链接：https://example.com，好", "https://example.com")]
    [InlineData("www.example.com/docs!", "www.example.com/docs")]
    [InlineData("http://localhost.dev:8080/x", "http://localhost.dev:8080/x")]
    [InlineData("https://mastodon.social/@tom", "https://mastodon.social/@tom")]
    public void LinksEndWhereTheSentenceGoesOn(string text, string link)
    {
        var token = Assert.Single(BeepText.Tokenize(text), t => t.Kind == BeepTextTokenKind.Link);
        Assert.Equal(link, token.Value);
        Assert.Equal(link, text.Substring(token.Start, token.Length));
    }

    [Theory]
    [InlineData("https://localhost/x")]
    [InlineData("http://127.0.0.1/")]
    [InlineData("https://user@example.com")]
    [InlineData("https://example.com:http/")]
    [InlineData("xhttps://example.com")]
    [InlineData("www.example")]
    [InlineData("example.com")]
    [InlineData("ftp://example.com")]
    [InlineData("javascript:alert(1)")]
    [InlineData("www.")]
    [InlineData("the www. prefix")]
    [InlineData("www.,")]
    [InlineData("访问www.。")]
    [InlineData("#x www.")]
    public void SomeAddressesAreNotLinks(string text) =>
        Assert.DoesNotContain(BeepText.Tokenize(text), t => t.Kind == BeepTextTokenKind.Link);

    [Fact]
    public void TagsAndMentionsWithinALinkAreTheLinks()
    {
        var parsed = BeepText.Parse("https://example.com/#top https://example.com/@tom #real");

        Assert.Equal(["real"], parsed.Hashtags);
        Assert.Empty(parsed.MentionHandles);
    }

    [Fact]
    public void LinksOpenElsewhereShowingTheirAddressWithoutTheScheme()
    {
        var html = BeepText.RenderHtml("see https://www.example.com/a?b=1&c=2 or www.example.org", new HashSet<string>());

        Assert.Equal(
            "see <a class=\"link\" href=\"https://www.example.com/a?b=1&amp;c=2\" target=\"_blank\" rel=\"nofollow noopener noreferrer ugc\">" +
            "<span class=\"link-hidden\">https://www.</span>example.com/a?b=1&amp;c=2</a> or " +
            "<a class=\"link\" href=\"https://www.example.org\" target=\"_blank\" rel=\"nofollow noopener noreferrer ugc\">" +
            "<span class=\"link-hidden\">www.</span>example.org</a>",
            html);
    }

    [Fact]
    public void AWwwLinkWithAnAddressInItsQueryStillOpensElsewhere()
    {
        var html = BeepText.RenderHtml("www.example.com/login?next=https://example.org", new HashSet<string>());

        Assert.Contains("href=\"https://www.example.com/login?next=https://example.org\"", html, StringComparison.Ordinal);
    }

    [Fact]
    public void LongLinksAreCutShortButKeptWhole()
    {
        var html = BeepText.RenderHtml("http://example.com/" + new string('a', 40), new HashSet<string>());

        Assert.Contains(
            "<span class=\"link-hidden\">http://</span><span class=\"link-cut\">example.com/" + new string('a', 18) + "</span>" +
            "<span class=\"link-hidden\">" + new string('a', 22) + "</span></a>",
            html,
            StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("plain text", false, false)]
    [InlineData("two\nlines\ttab", true, false)]
    [InlineData("two\nlines", false, true)]
    [InlineData("null\0", true, true)]
    [InlineData("emoji 😀 ok", true, false)]
    public void ForbiddenCharacters(string text, bool allowLineBreaks, bool forbidden) =>
        Assert.Equal(forbidden, TextRules.HasForbiddenCharacters(text, allowLineBreaks));

    [Fact]
    public void PageLimitsAreClamped()
    {
        Assert.Equal(PageRequest.DefaultLimit, PageRequest.Create().Limit);
        Assert.Equal(1, PageRequest.Create(limit: 0).Limit);
        Assert.Equal(PageRequest.MaxLimit, PageRequest.Create(limit: 1000).Limit);
    }
}
