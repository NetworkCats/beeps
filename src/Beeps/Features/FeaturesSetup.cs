using Beeps.Features.Accounts;
using Beeps.Features.Admin;
using Beeps.Features.Agents;
using Beeps.Features.Avatars;
using Beeps.Features.Beeps;
using Beeps.Features.Media;
using Beeps.Features.Moderation;
using Beeps.Features.Notifications;
using Beeps.Features.Profiles;
using Beeps.Features.Shared;
using Beeps.Features.Site;
using Beeps.Features.Timelines;
using Microsoft.AspNetCore.Identity;

namespace Beeps.Features;

public static class FeaturesSetup
{
    public static IServiceCollection AddBeepsFeatures(this IServiceCollection services)
    {
        services.AddOptions<SiteOptions>().BindConfiguration(SiteOptions.SectionName).ValidateDataAnnotations().ValidateOnStart();
        services.AddOptions<CaptchaOptions>().BindConfiguration(CaptchaOptions.SectionName).ValidateDataAnnotations().ValidateOnStart();
        services.AddOptions<AgentOptions>().BindConfiguration(AgentOptions.SectionName).ValidateDataAnnotations().ValidateOnStart();
        services.AddOptions<ModerationOptions>().BindConfiguration(ModerationOptions.SectionName);
        services.AddOptions<MediaOptions>().BindConfiguration(MediaOptions.SectionName).ValidateDataAnnotations().ValidateOnStart();

        services.AddSingleton<MediaUrls>();
        services.AddSingleton<AvatarUrls>();
        services.AddSingleton<IPasswordHasher<User>, PasswordHasher<User>>();
        services.AddScoped<AccountService>();
        services.AddScoped<SessionService>();
        services.AddScoped<AgentService>();
        services.AddScoped<AdminService>();
        services.AddScoped<BeepViews>();
        services.AddScoped<BeepService>();
        services.AddScoped<TimelineService>();
        services.AddScoped<ProfileService>();
        services.AddScoped<NotificationService>();
        services.AddScoped<MediaService>();
        services.AddScoped<AvatarService>();
        services.AddScoped<IOutboxHandler, AccountCodeEmailHandler>();
        services.AddScoped<IOutboxHandler, BeepNotificationsHandler>();
        services.AddScoped<IOutboxHandler, LikeNotificationHandler>();
        services.AddScoped<IOutboxHandler, DeleteMediaObjectsHandler>();
        services.AddScoped<IScheduledJob, AccountCleanupJob>();
        services.AddScoped<IScheduledJob, BeepCounterFlushJob>();
        services.AddScoped<IScheduledJob, MediaCleanupJob>();
        return services;
    }
}
