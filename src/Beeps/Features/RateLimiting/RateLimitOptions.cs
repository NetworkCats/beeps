using Microsoft.Extensions.Options;

namespace Beeps.Features.RateLimiting;

public sealed class RateLimitOptions
{
    public const string SectionName = "RateLimiting";

    public bool Enabled { get; set; } = true;

    public Dictionary<string, RateLimitPolicyOptions> Policies { get; set; } = [];
}

public sealed class RateLimitPolicyOptions
{
    /// <summary>All rules must pass; e.g. "1 per minute and 10 per day" for email codes.</summary>
    public List<RateLimitRuleOptions> Rules { get; set; } = [];
}

public sealed class RateLimitRuleOptions
{
    public int PermitLimit { get; set; }

    public TimeSpan Window { get; set; }
}

internal sealed class RateLimitOptionsValidator : IValidateOptions<RateLimitOptions>
{
    public ValidateOptionsResult Validate(string? name, RateLimitOptions options)
    {
        var failures = new List<string>();
        foreach (var policy in RateLimitPolicies.All)
        {
            if (!options.Policies.TryGetValue(policy, out var configured))
            {
                failures.Add($"RateLimiting:Policies:{policy} is not configured.");
                continue;
            }

            if (configured.Rules.Count == 0)
            {
                failures.Add($"RateLimiting:Policies:{policy} has no rules.");
            }

            foreach (var rule in configured.Rules)
            {
                if (rule.PermitLimit < 1)
                {
                    failures.Add($"RateLimiting:Policies:{policy}: PermitLimit must be at least 1.");
                }

                if (rule.Window < TimeSpan.FromSeconds(1))
                {
                    failures.Add($"RateLimiting:Policies:{policy}: Window must be at least one second.");
                }
            }
        }

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }
}
