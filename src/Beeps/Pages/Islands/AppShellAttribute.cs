using Beeps.Features.Auth;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace Beeps.Pages.Islands;

/// <summary>
/// A page whose content the app draws for signed-in people (PLAN 6.9). Opened signed in, nothing of the page runs: the
/// answer is the layout with a skeleton in place of the content (<c>_AppShell.cshtml</c>), and the app fetches the
/// page's data (<see cref="PageDataMiddleware"/>) and draws it, as it does for every page moved to after. Signed out the
/// page is drawn here as ever (search engines read it), and so is the answer to a form posted without the app.
/// </summary>
[AttributeUsage(AttributeTargets.Class)]
public sealed class AppShellAttribute : Attribute, IAsyncPageFilter
{
    public Task OnPageHandlerSelectionAsync(PageHandlerSelectedContext context) => Task.CompletedTask;

    public Task OnPageHandlerExecutionAsync(PageHandlerExecutingContext context, PageHandlerExecutionDelegate next)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(next);
        var http = context.HttpContext;
        if (!HttpMethods.IsGet(http.Request.Method) || !http.User.IsHuman() || IslandRequests.IsIslandRequest(http))
        {
            return next();
        }

        context.Result = new ViewResult { ViewName = "_AppShell" };
        return Task.CompletedTask;
    }
}
