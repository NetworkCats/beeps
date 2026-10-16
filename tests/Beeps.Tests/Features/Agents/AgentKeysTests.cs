using Beeps.Features.Agents;

namespace Beeps.Tests.Features.Agents;

public sealed class AgentKeysTests
{
    [Fact]
    public void KeysArePrefixedBase62WithFullEntropy()
    {
        var key = AgentKeys.Generate();

        Assert.Matches("^beeps_[0-9A-Za-z]{43}$", key);
        Assert.True(AgentKeys.IsWellFormed(key));
        Assert.NotEqual(key, AgentKeys.Generate());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("beeps_short")]
    [InlineData("other_0123456789012345678901234567890123456789012")]
    [InlineData("beeps_012345678901234567890123456789012345678-_")]
    public void MalformedKeysAreRecognisedWithoutALookup(string? key) => Assert.False(AgentKeys.IsWellFormed(key));

    [Fact]
    public void OnlyAShortPrefixAndTheHashAreKept()
    {
        var key = AgentKeys.Generate();

        Assert.Equal(key[..12], AgentKeys.DisplayPrefix(key));
        Assert.Matches("^[0-9a-f]{64}$", AgentKeys.Hash(key));
        Assert.Equal(AgentKeys.Hash(key), AgentKeys.Hash(key));
    }
}
