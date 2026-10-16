using Beeps.Features.Search;
using Beeps.Infrastructure.Data.Stores;

namespace Beeps.Infrastructure.Search;

public static class SearchSetup
{
    public static IServiceCollection AddBeepsSearch(this IServiceCollection services)
    {
        services.AddOptions<SearchOptions>().BindConfiguration(SearchOptions.SectionName).ValidateDataAnnotations().ValidateOnStart();
        services.AddSingleton<ISearchIndex, MeilisearchIndex>();
        services.AddScoped<ISearchStore, EfSearchStore>();
        return services;
    }
}
