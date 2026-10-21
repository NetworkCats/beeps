using System.Globalization;
using Beeps.Features.Email;
using Beeps.Tests.Support;

namespace Beeps.Tests.Features.Email;

public sealed class EmailLayoutTests
{
    private readonly EmailLayout layout = new(RealLocalizer.Create(), TestData.Site);

    private static readonly EmailContent Content = new("Subject", "Title", "Intro", "Outro", Code: "123456", CodeNote: "Note");

    [Fact]
    public void HasTheLogoAndTheSiteInTheFooter()
    {
        var email = layout.Compose("tom@example.com", "en", () => Content);

        Assert.Contains("src=\"https://beeps.test/images/logo.png\"", email.HtmlBody, StringComparison.Ordinal);
        Assert.Contains("The social network for AI agents.", email.HtmlBody, StringComparison.Ordinal);
        Assert.EndsWith("beeps · The social network for AI agents.\nhttps://beeps.test\n", email.TextBody, StringComparison.Ordinal);
    }

    [Fact]
    public void WritesItsOwnWordsInTheRecipientsLanguage()
    {
        var email = layout.Compose("tom@example.com", "zh-Hans", () => Content);

        Assert.Contains("<html lang=\"zh-Hans\">", email.HtmlBody, StringComparison.Ordinal);
        Assert.Contains("AI agents 的社交网络。", email.HtmlBody, StringComparison.Ordinal);
        Assert.Contains("AI agents 的社交网络。", email.TextBody, StringComparison.Ordinal);
    }

    [Fact]
    public void FallsBackToTheDefaultLanguageForOneNoLongerOffered()
    {
        var email = layout.Compose("tom@example.com", "fr", () => Content);

        Assert.Contains("<html lang=\"en\">", email.HtmlBody, StringComparison.Ordinal);
    }

    [Fact]
    public void RestoresTheCallersLanguage()
    {
        var before = CultureInfo.CurrentUICulture;

        layout.Compose("tom@example.com", "zh-Hans", () => Content);

        Assert.Equal(before, CultureInfo.CurrentUICulture);
    }

    [Fact]
    public void EncodesTheContent()
    {
        var email = layout.Compose("tom@example.com", "en", () => Content with { Intro = "<b>GitHub</b> & co" });

        Assert.Contains("&lt;b&gt;GitHub&lt;/b&gt; &amp; co", email.HtmlBody, StringComparison.Ordinal);
        Assert.DoesNotContain("<b>GitHub</b>", email.HtmlBody, StringComparison.Ordinal);
    }

    [Fact]
    public void LeavesOutTheCodeBoxWithoutACode()
    {
        var email = layout.Compose("tom@example.com", "en", () => Content with { Code = null, CodeNote = null });

        Assert.DoesNotContain("class=\"well", email.HtmlBody, StringComparison.Ordinal);
        Assert.Equal("Title\n\nIntro\n\nOutro\n\nbeeps · The social network for AI agents.\nhttps://beeps.test\n", email.TextBody);
    }
}
