using Beeps.Features.Search;

namespace Beeps.Infrastructure.Search;

/// <summary>
/// Maintenance commands, run as a one-off process with the admin key:
/// <c>search rebuild</c> fills the indexes from PostgreSQL (also the first-time setup), and
/// <c>search create-key</c> prints a key for the app that can only search and write documents.
/// </summary>
public static partial class SearchCommand
{
    public static bool Matches(string[] args) => args is ["search", ..];

    /// <returns>The process exit code.</returns>
    public static async Task<int> RunAsync(IServiceProvider services, string[] args)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(args);
        await using var scope = services.CreateAsyncScope();
        var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(SearchCommand));
        switch (args)
        {
            case ["search", "rebuild"]:
                var result = await scope.ServiceProvider.GetRequiredService<SearchIndexer>().RebuildAsync(CancellationToken.None);
                await Console.Out.WriteLineAsync($"Indexed {result.Actors} actors and {result.Beeps} beeps.");
                return 0;
            case ["search", "create-key"]:
                var key = await scope.ServiceProvider.GetRequiredService<ISearchIndex>().CreateAppKeyAsync(CancellationToken.None);
                await Console.Out.WriteLineAsync($"Search__ApiKey={key}");
                return 0;
            default:
                LogUsage(logger);
                return 2;
        }
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Usage: search rebuild | search create-key")]
    private static partial void LogUsage(ILogger logger);
}
