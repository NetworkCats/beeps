using Beeps.Features.Site;
using Beeps.Features.Translation;
using Beeps.Infrastructure.Moderation;
using Beeps.Infrastructure.OpenRouter;
using Beeps.Infrastructure.Translation;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Hosting.Internal;
using Microsoft.Extensions.Options;

namespace Beeps.Tests.Infrastructure.Translation;

public sealed class TranslationSetupTests
{
    [Fact]
    public void ProductionWithTranslationOnNeedsAnOpenRouterKeyEvenWithoutModeration()
    {
        using var services = Build("Production", translation: true, key: null);

        Assert.Throws<OptionsValidationException>(() => services.GetRequiredService<IOptions<OpenRouterOptions>>().Value);
    }

    [Theory]
    [InlineData("Production", false, null)]
    [InlineData("Production", true, "or-key")]
    [InlineData("Development", true, null)]
    public void OtherwiseTheSettingsAreFine(string environment, bool translation, string? key)
    {
        using var services = Build(environment, translation, key);

        Assert.Equal(key ?? "", services.GetRequiredService<IOptions<OpenRouterOptions>>().Value.ApiKey);
    }

    [Fact]
    public void DevelopmentWithoutAKeyGetsTheStandIn()
    {
        using var services = Build("Development", translation: true, key: null);
        using var scope = services.CreateScope();

        Assert.IsType<DevelopmentTranslator>(scope.ServiceProvider.GetRequiredService<ITranslator>());
    }

    [Fact]
    public void WithAKeyTheModelIsAskedEvenInDevelopment()
    {
        using var services = Build("Development", translation: true, key: "or-key");
        using var scope = services.CreateScope();

        Assert.IsType<OpenRouterTranslator>(scope.ServiceProvider.GetRequiredService<ITranslator>());
    }

    [Fact]
    public void ATranslationTakesLongerThanAReview()
    {
        using var services = Build("Production", translation: true, key: "or-key");

        Assert.Equal(TimeSpan.FromSeconds(60), services.GetRequiredService<IOptions<TranslationOptions>>().Value.RequestTimeout);
    }

    private static ServiceProvider Build(string environment, bool translation, string? key)
    {
        var settings = new Dictionary<string, string?>
        {
            ["Moderation:Enabled"] = "false",
            ["Translation:Enabled"] = translation ? "true" : "false",
            ["Site:BaseUrl"] = "https://beeps.test",
        };
        if (key is not null)
        {
            settings["OpenRouter:ApiKey"] = key;
        }

        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddSingleton<IHostEnvironment>(new HostingEnvironment { EnvironmentName = environment });
        services.AddSingleton(TimeProvider.System);
        services.AddLogging();
        services.AddOptions<SiteOptions>().BindConfiguration(SiteOptions.SectionName);
        services.AddBeepsModeration();
        services.AddBeepsTranslation();
        return services.BuildServiceProvider();
    }
}
