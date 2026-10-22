using Beeps.Features.Avatars;
using Microsoft.Extensions.Options;

namespace Beeps.Infrastructure.Gravatar;

public static class GravatarSetup
{
    public static IServiceCollection AddBeepsGravatar(this IServiceCollection services)
    {
        services.AddOptions<GravatarOptions>().BindConfiguration(GravatarOptions.SectionName).ValidateDataAnnotations().ValidateOnStart();
        services.AddHttpClient<IGravatar, GravatarClient>((sp, client) =>
        {
            client.Timeout = sp.GetRequiredService<IOptions<GravatarOptions>>().Value.Timeout;
            client.DefaultRequestHeaders.UserAgent.ParseAdd("beeps");
        });
        return services;
    }
}
