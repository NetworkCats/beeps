using Beeps.Features.Moderation;
using Beeps.Features.Site;
using Beeps.Infrastructure.Moderation;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Hosting.Internal;
using Microsoft.Extensions.Options;

namespace Beeps.Tests.Infrastructure.Moderation;

public sealed class ModerationSetupTests
{
    [Fact]
    public void ProductionWithModerationOnNeedsAnOpenRouterKey()
    {
        using var services = Build("Production", moderation: true, key: null);

        var failure = Assert.Throws<OptionsValidationException>(() => services.GetRequiredService<IOptions<OpenRouterOptions>>().Value);
        Assert.Contains("OpenRouter:ApiKey", failure.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Production", false, null)]
    [InlineData("Production", true, "or-key")]
    [InlineData("Development", true, null)]
    public void OtherwiseTheSettingsAreFine(string environment, bool moderation, string? key)
    {
        using var services = Build(environment, moderation, key);

        Assert.Equal(key ?? "", services.GetRequiredService<IOptions<OpenRouterOptions>>().Value.ApiKey);
    }

    [Fact]
    public void DevelopmentWithoutAKeyGetsTheStandIn()
    {
        using var services = Build("Development", moderation: true, key: null);
        using var scope = services.CreateScope();

        Assert.IsType<DevelopmentContentModerator>(scope.ServiceProvider.GetRequiredService<IContentModerator>());
    }

    [Fact]
    public void WithAKeyTheModelIsAskedEvenInDevelopment()
    {
        using var services = Build("Development", moderation: true, key: "or-key");
        using var scope = services.CreateScope();

        Assert.IsType<OpenRouterContentModerator>(scope.ServiceProvider.GetRequiredService<IContentModerator>());
    }

    private static ServiceProvider Build(string environment, bool moderation, string? key)
    {
        var settings = new Dictionary<string, string?>
        {
            ["Moderation:Enabled"] = moderation ? "true" : "false",
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
        return services.BuildServiceProvider();
    }
}
