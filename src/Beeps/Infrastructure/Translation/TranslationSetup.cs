using Beeps.Features.Site;
using Beeps.Features.Translation;
using Beeps.Infrastructure.OpenRouter;
using Microsoft.Extensions.Options;

namespace Beeps.Infrastructure.Translation;

public static class TranslationSetup
{
    /// <summary>After <see cref="Moderation.ModerationSetup.AddBeepsModeration"/>, which binds the model and OpenRouter's settings.</summary>
    public static IServiceCollection AddBeepsTranslation(this IServiceCollection services)
    {
        services.AddOptions<TranslationOptions>().BindConfiguration(TranslationOptions.SectionName).ValidateDataAnnotations().ValidateOnStart();
        services.AddOptions<OpenRouterOptions>()
            .Validate<IOptions<TranslationOptions>, IHostEnvironment>(
                (openRouter, translation, environment) => !translation.Value.Enabled || environment.IsDevelopment() || !string.IsNullOrWhiteSpace(openRouter.ApiKey),
                "OpenRouter:ApiKey is required while Translation:Enabled is on.");

        services.AddScoped<ITranslationJobs, EfTranslationJobs>();
        services.AddSingleton<ITranslationJobStore, PostgresTranslationJobStore>();
        services.AddScoped<TranslationProcessor>();
        services.AddHostedService<TranslationWorker>();
        services.AddHttpClient<OpenRouterTranslator>((sp, client) => OpenRouterHttp.Configure(
            client,
            sp.GetRequiredService<IOptions<OpenRouterOptions>>().Value,
            sp.GetRequiredService<IOptions<TranslationOptions>>().Value.RequestTimeout,
            sp.GetRequiredService<IOptions<SiteOptions>>().Value.BaseUrl));
        // Development without a key gets the stand-in, which marks the beep with the language it went into.
        services.AddScoped<ITranslator>(sp =>
            string.IsNullOrWhiteSpace(sp.GetRequiredService<IOptions<OpenRouterOptions>>().Value.ApiKey) && sp.GetRequiredService<IHostEnvironment>().IsDevelopment()
                ? new DevelopmentTranslator()
                : sp.GetRequiredService<OpenRouterTranslator>());
        return services;
    }
}
