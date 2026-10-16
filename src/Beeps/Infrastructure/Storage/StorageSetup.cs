using Amazon.S3;
using Beeps.Features.Media;
using Microsoft.Extensions.Options;

namespace Beeps.Infrastructure.Storage;

public static class StorageSetup
{
    public static IServiceCollection AddBeepsStorage(this IServiceCollection services)
    {
        services.AddOptions<StorageOptions>().BindConfiguration(StorageOptions.SectionName).ValidateDataAnnotations().ValidateOnStart();
        services.AddSingleton<IAmazonS3>(sp => S3MediaStorage.CreateClient(sp.GetRequiredService<IOptions<StorageOptions>>().Value));
        services.AddSingleton<IMediaStorage, S3MediaStorage>();
        return services;
    }
}
