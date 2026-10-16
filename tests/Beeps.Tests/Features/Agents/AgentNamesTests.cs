using Beeps.Features.Agents;

namespace Beeps.Tests.Features.Agents;

public sealed class AgentNamesTests
{
    [Theory]
    [InlineData("claude", true)]
    [InlineData("My_Helper_2", true)]
    [InlineData("abcdefghijabcdefghijabcdefghij", true)]
    [InlineData("abcdefghijabcdefghijabcdefghijk", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    [InlineData("two.dots", false)]
    [InlineData("with space", false)]
    [InlineData("助手", false)]
    public void OwnersPickHandleNamesAMentionCanHold(string? name, bool valid) =>
        Assert.Equal(valid, AgentNames.IsValidHandleName(name));

    [Theory]
    [InlineData("Claude Opus 4.5", "Claude")]
    [InlineData("claude-sonnet-4-5", "Claude")]
    [InlineData("anthropic/claude-haiku", "Claude")]
    [InlineData("gpt-4o", "GPT")]
    [InlineData("GPT-5 Codex", "GPT")]
    [InlineData("Qwen3-Coder", "Qwen")]
    [InlineData("deepseek-r1", "DeepSeek")]
    [InlineData("o3-mini", "o3")]
    [InlineData("通义千问 Max", "通义千问")]
    [InlineData("4.5", "Agent")]
    public void TheBrandIsAKnownNameOrTheFirstWord(string modelName, string brand) =>
        Assert.Equal(brand, AgentNames.Brand(modelName));

    [Fact]
    public void UnknownBrandsAreShortened() =>
        Assert.Equal(AgentNames.BrandMaxLength, AgentNames.Brand(new string('x', 60)).Length);

    [Fact]
    public void TheDisplayNameIsTheOwnersModel() =>
        Assert.Equal("tom's Claude", AgentNames.DisplayName("tom", "Claude"));

    [Theory]
    [InlineData("Claude", "tom.claude")]
    [InlineData("o3", "tom.o3")]
    [InlineData("通义千问", "tom.agent")]
    [InlineData("SuperLongModelBrandName", "tom.superlongmodelbrandn")]
    public void TheHandleIsTheOwnerDotTheBrand(string brand, string handle) =>
        Assert.Equal(handle, AgentNames.BaseHandle("tom", brand));

    [Fact]
    public void TakenHandlesGetTheSmallestFreeNumber()
    {
        Assert.Equal("Tom.claude", AgentNames.PickHandle("Tom.claude", new HashSet<string>()));
        Assert.Equal("Tom.claude2", AgentNames.PickHandle("Tom.claude", new HashSet<string> { "tom.claude" }));
        Assert.Equal("Tom.claude4", AgentNames.PickHandle("Tom.claude", new HashSet<string> { "tom.claude", "tom.claude2", "tom.claude3" }));
    }

    [Theory]
    [InlineData(" Claude Opus 4.5 ", "Claude Opus 4.5")]
    [InlineData("", null)]
    [InlineData("two\nlines", null)]
    public void ModelNamesAreTrimmedAndChecked(string input, string? expected) =>
        Assert.Equal(expected, AgentNames.CleanModelName(input));

    [Fact]
    public void ModelNamesHaveAMaximumLength()
    {
        Assert.NotNull(AgentNames.CleanModelName(new string('a', 100)));
        Assert.Null(AgentNames.CleanModelName(new string('a', 101)));
    }
}
