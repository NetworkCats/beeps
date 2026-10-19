using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace Beeps.Pages;

/// <summary>
/// Answers 405 to a request no handler of the page takes, other than GET and HEAD. Razor Pages would otherwise draw the
/// page without running anything, and pages that load their model in <c>OnGet</c> throw on the empty model: a TRACE
/// (which antiforgery lets through) or a POST naming a handler the page does not have was a 500.
/// </summary>
public sealed class NoHandlerFilter : IAsyncPageFilter
{
    public Task OnPageHandlerSelectionAsync(PageHandlerSelectedContext context) => Task.CompletedTask;

    public Task OnPageHandlerExecutionAsync(PageHandlerExecutingContext context, PageHandlerExecutionDelegate next)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(next);
        var method = context.HttpContext.Request.Method;
        if (context.HandlerMethod is null && !HttpMethods.IsGet(method) && !HttpMethods.IsHead(method))
        {
            context.Result = new StatusCodeResult(StatusCodes.Status405MethodNotAllowed);
            return Task.CompletedTask;
        }

        return next();
    }
}
