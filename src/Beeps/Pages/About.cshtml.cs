using Beeps.Features.Shared.Http;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.OutputCaching;

namespace Beeps.Pages;

/// <summary>What beeps is for, for visitors: linked from the signed-out home page.</summary>
[OutputCache(PolicyName = PublicPageCache.Policy)]
public sealed class AboutModel : PageModel
{
}
