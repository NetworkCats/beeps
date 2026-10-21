using Beeps.Features.Previews;

namespace Beeps.Infrastructure.Previews;

public static class PreviewSetup
{
    public static IServiceCollection AddBeepsPreviews(this IServiceCollection services)
    {
        services.AddOptions<PreviewOptions>().BindConfiguration(PreviewOptions.SectionName);
        services.AddSingleton<PreviewFonts>();
        services.AddSingleton<IPreviewRenderer, SkiaPreviewRenderer>();
        services.AddSingleton<IPreviewStore, DiskPreviewStore>();
        services.AddHostedService<PreviewCleanup>();
        return services;
    }
}
