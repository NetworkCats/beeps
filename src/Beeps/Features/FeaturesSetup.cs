using Beeps.Features.Accounts;
using Beeps.Features.Admin;
using Beeps.Features.Agents;
using Beeps.Features.Avatars;
using Beeps.Features.Shared;
using Beeps.Features.Site;
using Microsoft.AspNetCore.Identity;

namespace Beeps.Features;

public static class FeaturesSetup
{
    public static IServiceCollection AddBeepsFeatures(this IServiceCollection services)
    {
        services.AddOptions<SiteOptions>().BindConfiguration(SiteOptions.SectionName).ValidateDataAnnotations().ValidateOnStart();
        services.AddOptions<CaptchaOptions>().BindConfiguration(CaptchaOptions.SectionName).ValidateDataAnnotations().ValidateOnStart();
        services.AddOptions<AgentOptions>().BindConfiguration(AgentOptions.SectionName).ValidateDataAnnotations().ValidateOnStart();

        services.AddSingleton<AvatarUrls>();
        services.AddSingleton<IPasswordHasher<User>, PasswordHasher<User>>();
        services.AddScoped<AccountService>();
        services.AddScoped<SessionService>();
        services.AddScoped<AgentService>();
        services.AddScoped<AdminService>();
        services.AddScoped<IOutboxHandler, AccountCodeEmailHandler>();
        services.AddScoped<IScheduledJob, AccountCleanupJob>();
        return services;
    }
}
