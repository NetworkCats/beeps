using Beeps.Infrastructure;

namespace Beeps.Tests.Infrastructure;

public sealed class MemoryTrimmerTests
{
    private const long MB = 1L << 20;

    [Fact]
    public void TheFirstReadingOnlyStartsTheCount()
    {
        var policy = new MemoryTrimmer.TrimPolicy();

        Assert.False(policy.Due(0, 500 * MB));
    }

    [Fact]
    public void AQuietAppHoldingMuchMoreThanAtTheStartIsTrimmed()
    {
        var policy = new MemoryTrimmer.TrimPolicy();
        policy.Due(0, 80 * MB);

        Assert.True(policy.Due(10 * MB, 400 * MB));
    }

    [Fact]
    public void ABusyAppIsLeftToTheGc()
    {
        var policy = new MemoryTrimmer.TrimPolicy();
        policy.Due(0, 80 * MB);

        Assert.False(policy.Due(2000 * MB, 400 * MB));
        // Quiet from the next minute on.
        Assert.True(policy.Due(2010 * MB, 400 * MB));
    }

    [Fact]
    public void AQuietAppThatGrewLittleIsLeftAlone()
    {
        var policy = new MemoryTrimmer.TrimPolicy();
        policy.Due(0, 80 * MB);

        Assert.False(policy.Due(10 * MB, 120 * MB));
    }

    [Fact]
    public void AfterATrimOnlyNewGrowthCounts()
    {
        var policy = new MemoryTrimmer.TrimPolicy();
        policy.Due(0, 80 * MB);
        Assert.True(policy.Due(10 * MB, 400 * MB));
        policy.Trimmed(10 * MB, 200 * MB);

        Assert.False(policy.Due(20 * MB, 220 * MB));
        Assert.False(policy.Due(30 * MB, 200 * MB));
        Assert.True(policy.Due(40 * MB, 300 * MB));
    }
}
