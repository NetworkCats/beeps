using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Beeps.Pages;

[IgnoreAntiforgeryToken]
[ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
public sealed class ErrorModel : PageModel
{
    /// <summary>The resource prefix for the message: <c>Error.NotFound</c>, <c>Error.Forbidden</c>, <c>Error.RateLimited</c> or plain <c>Error</c>.</summary>
    public string Kind { get; private set; } = "Error";

    // Status-code re-execution keeps the original method, so a failed POST lands here as a POST.
    public void OnGet(int? code) => Kind = code switch
    {
        StatusCodes.Status404NotFound => "Error.NotFound",
        StatusCodes.Status403Forbidden => "Error.Forbidden",
        StatusCodes.Status429TooManyRequests => "Error.RateLimited",
        _ => "Error",
    };

    public void OnPost(int? code) => OnGet(code);
}
