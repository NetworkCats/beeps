using Microsoft.Extensions.Options;

namespace Beeps.Features.RateLimiting;

public static class RateLimitingSetup
{
    public static IServiceCollection AddBeepsRateLimiting(this IServiceCollection services)
    {
        services.AddOptions<RateLimitOptions>().BindConfiguration(RateLimitOptions.SectionName).ValidateOnStart();
        services.AddSingleton<IValidateOptions<RateLimitOptions>, RateLimitOptionsValidator>();
        services.AddSingleton<LocalRateLimiter>();
        services.AddSingleton<RateLimitService>();
        return services;
    }
}
