using System.Diagnostics;

namespace Beeps.Features.Shared.Http;

public static class ProblemDetailsSetup
{
    /// <summary>Extension member holding the stable error code (see <see cref="ErrorCodes"/>).</summary>
    public const string CodeKey = "code";

    public const string TraceIdKey = "trace_id";

    /// <summary>
    /// The status for an exception that reached the error handler: a bad request the server itself rejected
    /// (a body over the size limit, a malformed request) keeps its own status, 413 or 400; anything else is a 500.
    /// </summary>
    public static int StatusCodeFor(Exception exception) =>
        exception is BadHttpRequestException badRequest ? badRequest.StatusCode : StatusCodes.Status500InternalServerError;

    /// <summary>
    /// Every ProblemDetails response, including ones the framework produces on its own (404, 405,
    /// unhandled exceptions), carries a <c>code</c> so clients never have to parse <c>title</c>.
    /// </summary>
    public static IServiceCollection AddBeepsProblemDetails(this IServiceCollection services) =>
        services.AddProblemDetails(options => options.CustomizeProblemDetails = context =>
        {
            var problem = context.ProblemDetails;
            problem.Status ??= context.HttpContext.Response.StatusCode;
            problem.Extensions.TryAdd(CodeKey, ErrorCodes.ForStatus(problem.Status.Value));
            // The framework adds "traceId"; the API spells every member in snake_case.
            problem.Extensions.Remove("traceId");
            problem.Extensions.TryAdd(TraceIdKey, Activity.Current?.Id ?? context.HttpContext.TraceIdentifier);
        });
}
