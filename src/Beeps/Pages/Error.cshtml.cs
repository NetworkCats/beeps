using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Beeps.Pages;

[IgnoreAntiforgeryToken]
[ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
public sealed class ErrorModel : PageModel
{
    public bool IsNotFound { get; private set; }

    // Status-code re-execution keeps the original method, so a failed POST lands here as a POST.
    public void OnGet(int? code) => IsNotFound = code == StatusCodes.Status404NotFound;

    public void OnPost(int? code) => OnGet(code);
}
