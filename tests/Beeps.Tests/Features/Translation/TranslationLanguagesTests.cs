using Beeps.Features.Translation;

namespace Beeps.Tests.Features.Translation;

public sealed class TranslationLanguagesTests
{
    [Theory]
    [InlineData("en", new[] { "zh" })]
    [InlineData("zh", new[] { "en" })]
    [InlineData("ja", new[] { "en", "zh" })]
    [InlineData("und", new string[0])]
    public void EnglishGoesIntoChineseChineseIntoEnglishAndTheRestIntoBoth(string language, string[] targets) =>
        Assert.Equal(targets, TranslationLanguages.TargetsFrom(language));

    [Theory]
    [InlineData("en", "en")]
    [InlineData(" JA ", "ja")]
    [InlineData("zh-Hant", "zh")]
    [InlineData("pt_BR", "pt")]
    [InlineData("fil", "fil")]
    [InlineData("english", null)]
    [InlineData("e", null)]
    [InlineData("中文", null)]
    [InlineData("", null)]
    [InlineData(null, null)]
    public void CodesAreStoredWithoutRegionOrScript(string? code, string? normalized) =>
        Assert.Equal(normalized, TranslationLanguages.Normalize(code));

    [Theory]
    [InlineData("en", "en")]
    [InlineData("zh-Hans", "zh")]
    [InlineData("zh-CN", "zh")]
    public void ReadersGetTranslationsIntoTheLanguageTheyUseTheSiteIn(string culture, string target) =>
        Assert.Equal(target, TranslationLanguages.TargetFor(culture));

    [Fact]
    public void TheSwitchesOffAreThoseNotSentButTheReadersOwnKeepsWhatItHad()
    {
        // In English, "en" has no switch: what was saved for it (in Chinese) stays.
        var untranslated = TranslationLanguages.Untranslated(["en", "ja"], ["zh", "ko", "es", "fr", "de", "pt", "ru", "it", "ar", "hi"], TranslationLanguages.English);

        Assert.Equal(["en", "ja", "other"], untranslated);
        Assert.Empty(TranslationLanguages.Untranslated(["ja"], TranslationLanguages.Choices, TranslationLanguages.English));
    }

    [Fact]
    public void AReaderSeesTheTranslationIntoTheirLanguage()
    {
        var japanese = new BeepTranslations("ja", "Good morning", "早上好");

        Assert.Equal(new TranslationView("ja", "Good morning"), new ReaderLanguages("en", []).Translate(japanese));
        Assert.Equal(new TranslationView("ja", "早上好"), new ReaderLanguages("zh", []).Translate(japanese));
    }

    [Fact]
    public void ABeepInTheReadersLanguageOrOneTheyTurnedOffIsShownAsWritten()
    {
        var english = new BeepTranslations("en", null, "你好");
        var korean = new BeepTranslations("ko", "Hello", "你好");
        var swahili = new BeepTranslations("sw", "Hello", "你好");

        Assert.Null(new ReaderLanguages("en", []).Translate(english));
        Assert.Null(new ReaderLanguages("en", ["ko"]).Translate(korean));
        Assert.NotNull(new ReaderLanguages("en", ["ko"]).Translate(swahili));
        // A language the settings do not name is one of "other languages".
        Assert.Null(new ReaderLanguages("en", ["other"]).Translate(swahili));
        Assert.NotNull(new ReaderLanguages("en", ["other"]).Translate(korean));
    }

    [Fact]
    public void WithoutATranslationThereIsNothingToShow()
    {
        Assert.Null(new ReaderLanguages("zh", []).Translate(null));
        Assert.Null(new ReaderLanguages("zh", []).Translate(new BeepTranslations("und", null, null)));
    }
}
