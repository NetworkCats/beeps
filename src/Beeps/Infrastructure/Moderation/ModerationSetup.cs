using Beeps.Features.Moderation;
using Beeps.Features.Site;
using Microsoft.Extensions.Options;

namespace Beeps.Infrastructure.Moderation;

public static class ModerationSetup
{
    public static IServiceCollection AddBeepsModeration(this IServiceCollection services)
    {
        services.AddOptions<ModerationOptions>().BindConfiguration(ModerationOptions.SectionName).ValidateDataAnnotations().ValidateOnStart();
        services.AddOptions<OpenRouterOptions>()
            .BindConfiguration(OpenRouterOptions.SectionName)
            .Validate<IOptions<ModerationOptions>, IHostEnvironment>(
                (openRouter, moderation, environment) => !moderation.Value.Enabled || environment.IsDevelopment() || !string.IsNullOrWhiteSpace(openRouter.ApiKey),
                "OpenRouter:ApiKey is required while Moderation:Enabled is on.")
            .ValidateOnStart();

        services.AddScoped<IModerationJobs, EfModerationJobs>();
        services.AddSingleton<IModerationJobStore, PostgresModerationJobStore>();
        services.AddScoped<ModerationProcessor>();
        services.AddHostedService<ModerationWorker>();
        services.AddHttpClient<OpenRouterContentModerator>((sp, client) => OpenRouterHttp.Configure(
            client,
            sp.GetRequiredService<IOptions<OpenRouterOptions>>().Value,
            sp.GetRequiredService<IOptions<ModerationOptions>>().Value,
            sp.GetRequiredService<IOptions<SiteOptions>>().Value.BaseUrl));
        // Development without a key gets the stand-in, which allows everything but marked test beeps (PLAN 9.0).
        services.AddScoped<IContentModerator>(sp =>
            string.IsNullOrWhiteSpace(sp.GetRequiredService<IOptions<OpenRouterOptions>>().Value.ApiKey) && sp.GetRequiredService<IHostEnvironment>().IsDevelopment()
                ? new DevelopmentContentModerator()
                : sp.GetRequiredService<OpenRouterContentModerator>());
        return services;
    }
}
