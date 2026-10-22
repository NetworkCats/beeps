using Beeps.Features.Beeps;
using Beeps.Features.Live;
using Beeps.Features.Translation;
using Beeps.Infrastructure.Translation;
using Beeps.Tests.Support;
using Microsoft.Extensions.Logging.Abstractions;

namespace Beeps.Tests.Features.Translation;

public sealed class TranslationServiceTests
{
    private static readonly Guid BeepId = Guid.NewGuid();

    private readonly ITranslationStore store = Substitute.For<ITranslationStore>();
    private readonly ITranslator translator = Substitute.For<ITranslator>();
    private readonly ILiveEventStore liveEvents = Substitute.For<ILiveEventStore>();
    private readonly ImmediateUnitOfWork unitOfWork = new();
    private readonly List<BeepTranslation> added = [];

    public TranslationServiceTests() =>
        store.When(s => s.Add(Arg.Any<BeepTranslation>())).Do(ci => added.Add(ci.Arg<BeepTranslation>()));

    [Fact]
    public async Task AnEnglishBeepIsStoredWithItsChineseTranslationAndRedrawn()
    {
        Subject("good morning");
        translator.TranslateAsync("good morning", Arg.Any<CancellationToken>()).Returns(Verdict("en", "good morning", " 早上好 "));

        await Create().TranslateAsync(BeepId, TestSupport.Ct);

        var translation = Assert.Single(added);
        Assert.Equal((BeepId, "en", null, "早上好"), (translation.BeepId, translation.Language, translation.English, translation.Chinese));
        Assert.Equal(("test-model", "TestProvider", 420, (int?)99, TestData.Now), (translation.Model, translation.Provider, translation.LatencyMs, translation.Tokens, translation.CreatedAt));
        Assert.Equal(1, unitOfWork.Saves);
        Assert.Equal([$$"""{"ids":["{{BeepId:D}}"]}"""], TestData.LiveEvents(liveEvents, LiveEventTypes.BeepsChanged));
    }

    [Fact]
    public async Task ABeepInAnotherLanguageGetsBoth()
    {
        Subject("おはよう");
        translator.TranslateAsync(default!, TestSupport.Ct).ReturnsForAnyArgs(Verdict("ja", "Good morning", "早上好"));

        await Create().TranslateAsync(BeepId, TestSupport.Ct);

        var translation = Assert.Single(added);
        Assert.Equal(("ja", "Good morning", "早上好"), (translation.Language, translation.English, translation.Chinese));
    }

    [Fact]
    public async Task ABeepWithNothingToTranslateIsRecordedSoItIsNotAskedAgain()
    {
        Subject("https://example.com 🎉");
        translator.TranslateAsync(default!, TestSupport.Ct).ReturnsForAnyArgs(Verdict("und", "", ""));

        await Create().TranslateAsync(BeepId, TestSupport.Ct);

        var translation = Assert.Single(added);
        Assert.Equal(("und", null, null), (translation.Language, translation.English, translation.Chinese));
        Assert.Empty(TestData.LiveEvents(liveEvents, LiveEventTypes.BeepsChanged));
    }

    [Fact]
    public async Task ATranslationTheSameAsTheBeepShowsNothing()
    {
        Subject("GitHub");
        translator.TranslateAsync(default!, TestSupport.Ct).ReturnsForAnyArgs(Verdict("en", "", "GitHub"));

        await Create().TranslateAsync(BeepId, TestSupport.Ct);

        Assert.Null(Assert.Single(added).Chinese);
        Assert.Empty(TestData.LiveEvents(liveEvents, LiveEventTypes.BeepsChanged));
    }

    [Fact]
    public async Task AMissingTranslationIsABadAnswerToRetry()
    {
        Subject("おはよう");
        translator.TranslateAsync(default!, TestSupport.Ct).ReturnsForAnyArgs(Verdict("ja", "Good morning", " "));

        await Assert.ThrowsAsync<TranslationException>(() => Create().TranslateAsync(BeepId, TestSupport.Ct));

        Assert.Empty(added);
    }

    [Fact]
    public async Task AnOverlongTranslationIsCutToItsColumn()
    {
        Subject("长文");
        translator.TranslateAsync(default!, TestSupport.Ct).ReturnsForAnyArgs(Verdict("zh", new string('w', 5000), ""));

        await Create().TranslateAsync(BeepId, TestSupport.Ct);

        Assert.Equal(BeepTranslation.TextMaxLength, Assert.Single(added).English?.Length);
    }

    [Theory]
    [InlineData(ModerationStatus.Pending, false, false, "hi")]
    [InlineData(ModerationStatus.Hidden, false, false, "hi")]
    [InlineData(ModerationStatus.Failed, false, false, "hi")]
    [InlineData(ModerationStatus.Allowed, true, false, "hi")]
    [InlineData(ModerationStatus.Allowed, false, true, "hi")]
    [InlineData(ModerationStatus.HumanAllowed, false, false, "")]
    public async Task OnlyAShownBeepWithTextIsTranslatedAndOnlyOnce(ModerationStatus status, bool deleted, bool translated, string text)
    {
        store.GetSubjectAsync(BeepId, Arg.Any<CancellationToken>()).Returns(new TranslationSubject(BeepId, text, status, deleted, translated));

        await Create().TranslateAsync(BeepId, TestSupport.Ct);

        await translator.DidNotReceiveWithAnyArgs().TranslateAsync(default!, TestSupport.Ct);
        Assert.Empty(added);
    }

    [Fact]
    public async Task ABeepKeptByAnAdminIsTranslated()
    {
        store.GetSubjectAsync(BeepId, Arg.Any<CancellationToken>()).Returns(new TranslationSubject(BeepId, "hi", ModerationStatus.HumanAllowed, false, false));
        translator.TranslateAsync(default!, TestSupport.Ct).ReturnsForAnyArgs(Verdict("en", "", "嗨"));

        await Create().TranslateAsync(BeepId, TestSupport.Ct);

        Assert.Single(added);
    }

    [Fact]
    public async Task TheDevelopmentTranslatorMarksWhatItTranslatesAndFailsOnRequest()
    {
        var development = new DevelopmentTranslator();

        var english = await development.TranslateAsync("hello", TestSupport.Ct);
        var chinese = await development.TranslateAsync("你好", TestSupport.Ct);

        Assert.Equal(("en", null, "[zh] hello"), (english.Language, english.English, english.Chinese));
        Assert.Equal(("zh", "[en] 你好", null), (chinese.Language, chinese.English, chinese.Chinese));
        await Assert.ThrowsAsync<TranslationException>(() => development.TranslateAsync(DevelopmentTranslator.FailMarker, TestSupport.Ct));
    }

    private void Subject(string text) =>
        store.GetSubjectAsync(BeepId, Arg.Any<CancellationToken>()).Returns(new TranslationSubject(BeepId, text, ModerationStatus.Allowed, false, false));

    private static TranslationVerdict Verdict(string language, string english, string chinese) =>
        new(language, english, chinese, "test-model", "TestProvider", 420, 99);

    private TranslationService Create() =>
        new(store, translator, unitOfWork, TestData.Live(liveEvents), TestData.Time(), NullLogger<TranslationService>.Instance);
}
