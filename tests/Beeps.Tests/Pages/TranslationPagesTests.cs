using System.Net;
using System.Text.Json;
using Beeps.Features.Accounts;
using Beeps.Features.Beeps;
using Beeps.Features.Shared;
using Beeps.Features.Translation;
using Beeps.Infrastructure.Crawlers;
using Beeps.Tests.Support;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Beeps.Tests.Pages;

public sealed class TranslationPagesTests : IDisposable
{
    private readonly BeepsWebApplicationFactory factory = new();

    [Fact]
    public async Task ASignedInReaderSeesBeepsInOtherLanguagesTranslatedIntoTheirOwn()
    {
        var user = WebAuth.ArrangeUser(factory);
        var (japanese, english, mine) = ArrangeThread(user);
        using var client = await WebAuth.SignInAsync(factory, user);

        var items = await ThreadAsync(client, japanese.Id);

        var translation = items[0].GetProperty("translation");
        Assert.Equal("ja", translation.GetProperty("lang").GetString());
        // Linked as the text is.
        Assert.Equal("Good morning <a class=\"hashtag\" href=\"/tags/beeps\">#beeps</a>", translation.GetProperty("html").GetString());
        // The original comes along, a click away.
        Assert.StartsWith("おはよう ", items[0].GetProperty("html").GetString(), StringComparison.Ordinal);
        // Already in their language, and their own, they read as written.
        Assert.False(items[1].TryGetProperty("translation", out _));
        Assert.False(items[2].TryGetProperty("translation", out _));
        Assert.Equal([japanese.Id, english.Id, mine.Id], items.Select(b => b.GetProperty("id").GetGuid()));
    }

    [Fact]
    public async Task AChineseReaderGetsChineseTranslationsOfEnglishToo()
    {
        var user = WebAuth.ArrangeUser(factory, locale: "zh-Hans");
        var (japanese, _, _) = ArrangeThread(user);
        using var client = await WebAuth.SignInAsync(factory, user);

        var items = await ThreadAsync(client, japanese.Id);

        Assert.Equal("早上好 <a class=\"hashtag\" href=\"/tags/beeps\">#beeps</a>", items[0].GetProperty("translation").GetProperty("html").GetString());
        Assert.Equal(("en", "你好"), (items[1].GetProperty("translation").GetProperty("lang").GetString(), items[1].GetProperty("translation").GetProperty("html").GetString()));
    }

    [Fact]
    public async Task ALanguageSwitchedOffIsShownAsWritten()
    {
        var user = WebAuth.ArrangeUser(factory);
        user.UntranslatedLanguages = ["ja"];
        var (japanese, _, _) = ArrangeThread(user);
        using var client = await WebAuth.SignInAsync(factory, user);

        var items = await ThreadAsync(client, japanese.Id);

        Assert.False(items[0].TryGetProperty("translation", out _));
    }

    [Theory]
    [InlineData("en", "Good morning <a class=\"hashtag\" href=\"/tags/beeps\">#beeps</a>")]
    [InlineData("zh-CN", "早上好 <a class=\"hashtag\" href=\"/tags/beeps\">#beeps</a>")]
    public async Task SignedOutVisitorsReadBeepsTranslatedIntoTheLanguageTheySeeTheSiteIn(string acceptLanguage, string expected)
    {
        var (japanese, _, _) = ArrangeThread(null);
        using var client = WebAuth.NewClient(factory);
        client.DefaultRequestHeaders.AcceptLanguage.ParseAdd(acceptLanguage);

        var items = await ThreadAsync(client, japanese.Id);

        Assert.Equal(expected, items[0].GetProperty("translation").GetProperty("html").GetString());
    }

    [Fact]
    public async Task CrawlersGetTheBeepsAsWritten()
    {
        factory.Services.GetRequiredService<CrawlerAddressList>().Use([IPNetwork.Parse("66.249.64.0/27")]);
        var (japanese, _, _) = ArrangeThread(null);

        var context = await factory.Server.SendAsync(c =>
        {
            c.Request.Method = HttpMethods.Get;
            c.Request.Path = $"/beeps/{japanese.Id}";
            c.Connection.RemoteIpAddress = IPAddress.Parse("66.249.64.7");
        }, TestSupport.Ct);
        using var reader = new StreamReader(context.Response.Body);
        var html = await reader.ReadToEndAsync(TestSupport.Ct);

        Assert.Contains("おはよう", html, StringComparison.Ordinal);
        Assert.DoesNotContain("Good morning", html, StringComparison.Ordinal);
        Assert.DoesNotContain("\"translation\"", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheSwitchesSaveTheLanguagesLeftOffAndShowThem()
    {
        var user = WebAuth.ArrangeUser(factory);
        using var client = await WebAuth.SignInAsync(factory, user);

        // An unchecked switch is not sent at all.
        using var response = await WebAuth.PostFormAsync(client, "/settings/translation", "/settings/translation?handler=Translation",
            ("Translate", "zh"), ("Translate", "ko"), ("Translate", "es"), ("Translate", "fr"), ("Translate", "de"), ("Translate", "pt"),
            ("Translate", "ru"), ("Translate", "it"), ("Translate", "ar"), ("Translate", "hi"));
        var page = await Islands.PageAsync(client, "/settings/translation");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/settings/translation", response.Headers.Location?.OriginalString);
        Assert.Equal(["ja", "other"], user.UntranslatedLanguages);
        var settings = Islands.Props(page, "settings");
        Assert.Equal("translation", settings.GetProperty("section").GetString());
        var translation = settings.GetProperty("translation");
        // Every language but their own has a switch.
        Assert.Equal(TranslationLanguages.Choices.Where(l => l != "en"), translation.GetProperty("languages").EnumerateArray().Select(l => l.GetString()));
        Assert.Equal(["ja", "other"], translation.GetProperty("untranslated").EnumerateArray().Select(l => l.GetString()));
    }

    /// <summary>A thread of a Japanese beep, an English reply and one by <paramref name="reader"/>, each translated.</summary>
    private (BeepRow Japanese, BeepRow English, BeepRow Mine) ArrangeThread(User? reader)
    {
        var alice = TestBeeps.Human("alice");
        var japanese = TestBeeps.Row("おはよう #beeps", alice) with { Translation = new BeepTranslations("ja", "Good morning #beeps", "早上好 #beeps") };
        var english = TestBeeps.Row("hello", alice, replyTo: japanese.Id, root: japanese.Id) with { Translation = new BeepTranslations("en", null, "你好") };
        var mine = TestBeeps.Row("bonjour", reader is null ? TestBeeps.Human() : new ActorRow(reader.Id, ActorKind.Human, "tom", "tom", null), replyTo: japanese.Id, root: japanese.Id)
            with { Translation = new BeepTranslations("fr", "hello", "你好") };
        TestBeeps.Arrange(factory, japanese, english, mine);
        factory.Beeps.ListThreadAsync(japanese.Id, null, PageRequest.MaxLimit, reader?.Id, Arg.Any<CancellationToken>())
            .Returns(new CursorPage<BeepRow>([japanese, english, mine], null));
        return (japanese, english, mine);
    }

    private static async Task<List<JsonElement>> ThreadAsync(HttpClient client, Guid beepId)
    {
        var page = await Islands.PageAsync(client, $"/beeps/{beepId}");
        return [.. Islands.Props(page, "thread").GetProperty("items").EnumerateArray()];
    }

    public void Dispose() => factory.Dispose();
}
