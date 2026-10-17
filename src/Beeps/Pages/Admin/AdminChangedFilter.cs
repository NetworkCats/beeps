using Beeps.Features.Live;
using Microsoft.AspNetCore.Mvc.Filters;

namespace Beeps.Pages.Admin;

/// <summary>
/// Every admin action is a post to an admin page, and every one of them changes what the admin pages show (each is
/// written to the audit log, at least): once it is done, other admins' open pages draw their figures and lists again.
/// </summary>
public sealed class AdminChangedFilter : IAsyncPageFilter
{
    public Task OnPageHandlerSelectionAsync(PageHandlerSelectedContext context) => Task.CompletedTask;

    public async Task OnPageHandlerExecutionAsync(PageHandlerExecutingContext context, PageHandlerExecutionDelegate next)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(next);
        var executed = await next();
        if (HttpMethods.IsPost(context.HttpContext.Request.Method) && executed.Exception is null)
        {
            await context.HttpContext.RequestServices.GetRequiredService<LiveUpdates>().AdminChangedAsync(context.HttpContext.RequestAborted);
        }
    }
}
