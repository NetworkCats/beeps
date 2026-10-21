namespace Beeps.Infrastructure.ErrorReporting;

/// <summary>
/// Errors to Sentry: exceptions nobody caught and whatever is logged at Error or above. Only with a DSN
/// (<c>Sentry:Dsn</c>), which production alone is given (docs/INSTALL.md); without one Sentry is not set up at all.
/// Any other <c>Sentry:*</c> option (e.g. <c>Sentry__TracesSampleRate</c>) can be set in configuration.
/// </summary>
public static class ErrorReportingSetup
{
    public static IWebHostBuilder UseBeepsErrorReporting(this IWebHostBuilder builder, IConfiguration configuration)
    {
        if (string.IsNullOrEmpty(configuration["Sentry:Dsn"]))
        {
            return builder;
        }

        return builder.UseSentry(options =>
        {
            // A call to OpenRouter, Meilisearch or Turnstile that fails is the caller's to report, once a minute while
            // the service is away (LogThrottle), not an event of its own each time.
            options.CaptureFailedRequests = false;
            // The query string can hold an OAuth code; the path is enough to find the page.
            options.SetBeforeSend(sentryEvent =>
            {
                sentryEvent.Request.QueryString = null;
                return sentryEvent;
            });
            options.SetBeforeSendTransaction(transaction =>
            {
                transaction.Request.QueryString = null;
                return transaction;
            });
            // Breadcrumbs come from every log at Information or above, whatever Logging:LogLevel says; the framework's
            // "Request starting" lines would bring the query string back, and the event has the request already.
            options.SetBeforeBreadcrumb(breadcrumb =>
                breadcrumb.Category == "Microsoft.AspNetCore.Hosting.Diagnostics" ? null : breadcrumb);
        });
    }
}
