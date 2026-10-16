using Beeps.Features.Auth;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Beeps.Pages.Account;

public sealed class LogoutModel : PageModel
{
    public IActionResult OnGet() => LocalRedirect("~/");

    public async Task<IActionResult> OnPostAsync()
    {
        await HttpContext.SignOutHumanAsync();
        return LocalRedirect("~/");
    }
}
