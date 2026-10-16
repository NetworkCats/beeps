using Beeps.Features.RateLimiting;
using Microsoft.Extensions.Configuration;

namespace Beeps.Tests.Features.RateLimiting;

public sealed class RateLimitOptionsValidatorTests
{
    private readonly RateLimitOptionsValidator validator = new();

    [Fact]
    public void ShippedConfigurationDefinesEveryPolicy()
    {
        var configuration = new ConfigurationBuilder()
            .AddJsonFile(Path.Combine(AppContext.BaseDirectory, "appsettings.json"))
            .Build();
        var options = configuration.GetSection(RateLimitOptions.SectionName).Get<RateLimitOptions>()!;

        var result = validator.Validate(null, options);

        Assert.True(result.Succeeded, result.FailureMessage);
    }

    [Fact]
    public void MissingPolicyFails()
    {
        var options = Complete();
        options.Policies.Remove(RateLimitPolicies.Login);

        var result = validator.Validate(null, options);

        Assert.True(result.Failed);
        Assert.Contains("Login", result.FailureMessage, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(0, 60)]
    [InlineData(5, 0)]
    public void RuleNeedsAPositiveLimitAndWindow(int limit, int windowSeconds)
    {
        var options = Complete();
        options.Policies[RateLimitPolicies.Search].Rules[0] = new RateLimitRuleOptions { PermitLimit = limit, Window = TimeSpan.FromSeconds(windowSeconds) };

        Assert.True(validator.Validate(null, options).Failed);
    }

    [Fact]
    public void PolicyWithoutRulesFails()
    {
        var options = Complete();
        options.Policies[RateLimitPolicies.Search].Rules.Clear();

        Assert.True(validator.Validate(null, options).Failed);
    }

    private static RateLimitOptions Complete()
    {
        var options = new RateLimitOptions();
        foreach (var policy in RateLimitPolicies.All)
        {
            options.Policies[policy] = new RateLimitPolicyOptions { Rules = [new RateLimitRuleOptions { PermitLimit = 10, Window = TimeSpan.FromMinutes(1) }] };
        }

        return options;
    }
}
