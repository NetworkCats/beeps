using Beeps.Features.Beeps;
using Beeps.Features.Moderation;
using Beeps.Features.RateLimiting;
using Beeps.Features.Shared;
using Beeps.Tests.Support;
using Microsoft.Extensions.Logging.Abstractions;

namespace Beeps.Tests.Features.Moderation;

public sealed class ReportServiceTests
{
    private static readonly Guid Alice = Guid.NewGuid();
    private static readonly Guid Tom = Guid.NewGuid();
    private static readonly Guid TomsBeep = Guid.NewGuid();

    private readonly IModerationStore store = Substitute.For<IModerationStore>();
    private readonly IBeepStore beeps = Substitute.For<IBeepStore>();
    private readonly IRateLimitStore rateLimits = Substitute.For<IRateLimitStore>();
    private RateLimitDecision decision = RateLimitDecision.Allowed;

    public ReportServiceTests()
    {
        rateLimits.TryAcquireAsync(default!, TestSupport.Ct).ReturnsForAnyArgs(_ => decision);
        beeps.FindVisibleAsync(TomsBeep, Arg.Any<Guid?>(), Arg.Any<CancellationToken>()).Returns(new BeepRef(TomsBeep, Tom, null, TomsBeep, null, 0, 0));
        store.AddReportAsync(default!, TestSupport.Ct).ReturnsForAnyArgs(true);
    }

    [Fact]
    public async Task AReportIsRecordedWithItsReasonAndNote()
    {
        var result = await Create().ReportAsync(Viewer.Human(Alice), TomsBeep, ModerationCategories.Spam, "  same link\r\nevery hour  ", TestSupport.Ct);

        Assert.True(result.IsSuccess);
        await store.Received(1).AddReportAsync(
            Arg.Is<Report>(r => r.BeepId == TomsBeep && r.ReporterId == Alice && r.Reason == "spam" && r.Note == "same link\nevery hour" && r.CreatedAt == TestData.Now && r.ResolvedAt == null),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AnEmptyNoteIsNoNote()
    {
        await Create().ReportAsync(Viewer.Human(Alice), TomsBeep, ModerationCategories.Other, "   ", TestSupport.Ct);

        await store.Received(1).AddReportAsync(Arg.Is<Report>(r => r.Note == null), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(null, null, ReportErrorCodes.ReasonInvalid)]
    [InlineData("rude", null, ReportErrorCodes.ReasonInvalid)]
    [InlineData("spam", "bad\u0007", ReportErrorCodes.NoteInvalid)]
    public async Task ReasonAndNoteAreChecked(string? reason, string? note, string code)
    {
        var result = await Create().ReportAsync(Viewer.Human(Alice), TomsBeep, reason, note, TestSupport.Ct);

        Assert.Equal((ErrorKind.Validation, code), (result.Error?.Kind, result.Error?.Code));
        await store.DidNotReceiveWithAnyArgs().AddReportAsync(default!, TestSupport.Ct);
    }

    [Fact]
    public async Task ATooLongNoteIsRejected()
    {
        var result = await Create().ReportAsync(Viewer.Human(Alice), TomsBeep, "spam", new string('a', Report.NoteMaxLength + 1), TestSupport.Ct);

        Assert.Equal(ReportErrorCodes.NoteInvalid, result.Error?.Code);
    }

    [Fact]
    public async Task AgentsOwnBeepsAndMissingBeepsCannotBeReported()
    {
        var service = Create();

        var agent = await service.ReportAsync(Viewer.Agent(Guid.NewGuid(), Alice), TomsBeep, "spam", null, TestSupport.Ct);
        var own = await service.ReportAsync(Viewer.Human(Tom), TomsBeep, "spam", null, TestSupport.Ct);
        var missing = await service.ReportAsync(Viewer.Human(Alice), Guid.NewGuid(), "spam", null, TestSupport.Ct);

        Assert.Equal(ReportErrorCodes.AgentsCannotReport, agent.Error?.Code);
        Assert.Equal(ReportErrorCodes.CannotReportOwn, own.Error?.Code);
        Assert.Equal(ErrorKind.NotFound, missing.Error?.Kind);
        await store.DidNotReceiveWithAnyArgs().AddReportAsync(default!, TestSupport.Ct);
    }

    [Fact]
    public async Task ReportingIsRateLimited()
    {
        decision = RateLimitDecision.Denied(TimeSpan.FromMinutes(5));

        var result = await Create().ReportAsync(Viewer.Human(Alice), TomsBeep, "spam", null, TestSupport.Ct);

        Assert.Equal(ErrorKind.RateLimited, result.Error?.Kind);
        await rateLimits.Received(1).TryAcquireAsync(
            Arg.Is<IReadOnlyList<RateLimitCounter>>(c => TestData.Policies(c).SequenceEqual(new[] { RateLimitPolicies.Report })), Arg.Any<CancellationToken>());
        await store.DidNotReceiveWithAnyArgs().AddReportAsync(default!, TestSupport.Ct);
    }

    private ReportService Create() =>
        new(store, beeps, TestData.RateLimits(rateLimits), TestData.Time(), new TestLocalizer(), NullLogger<ReportService>.Instance);
}
