using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Beeps.Pages.Admin;

/// <summary>The overview arrives in stage 6; until then the admin area opens on the user list.</summary>
public sealed class IndexModel : PageModel
{
    public IActionResult OnGet() => RedirectToPage("/Admin/Users/Index");
}
