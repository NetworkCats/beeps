using Beeps.Features.Auth;
using Beeps.Features.Beeps;
using Beeps.Features.Shared;
using Beeps.Features.Shared.Http;
using Beeps.Features.Site;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Localization;

namespace Beeps.Pages.Beep;

/// <summary>
/// Like, unlike and delete, from the buttons on every beep. A plain form post goes back to the page it came
/// from; the like button's script sends <c>X-Requested-With: fetch</c> and gets just a status code.
/// </summary>
public sealed class ActionsModel(BeepService beeps, IStringLocalizer<SharedResource> localizer) : PageModel
{
    [BindProperty(SupportsGet = true)] public string? ReturnUrl { get; set; }

    public IActionResult OnGet(Guid id) => LocalRedirect(SitePaths.Beep(id));

    public async Task<IActionResult> OnPostLikeAsync(Guid id, CancellationToken cancellationToken) =>
        Done(await beeps.LikeAsync(User.Viewer(), id, cancellationToken), id, deleted: false);

    public async Task<IActionResult> OnPostUnlikeAsync(Guid id, CancellationToken cancellationToken) =>
        Done(await beeps.UnlikeAsync(User.Viewer(), id, cancellationToken), id, deleted: false);

    public async Task<IActionResult> OnPostDeleteAsync(Guid id, CancellationToken cancellationToken) =>
        Done(await beeps.DeleteAsync(User.Viewer(), id, cancellationToken), id, deleted: true);

    private IActionResult Done(Result result, Guid id, bool deleted)
    {
        if (Request.Headers.XRequestedWith == AuthSetup.FetchHeaderValue)
        {
            return result.IsSuccess ? StatusCode(StatusCodes.Status204NoContent) : StatusCode(ErrorHttpResult.StatusCodeFor(result.Error.Kind));
        }

        var back = Url.IsLocalUrl(ReturnUrl) ? ReturnUrl : SitePaths.Beep(id);
        this.Notify(result, deleted ? localizer["Beep.Deleted"] : null);
        if (!result.IsSuccess)
        {
            return LocalRedirect(back);
        }

        if (!deleted)
        {
            return LocalRedirect($"{back}#{SitePaths.BeepAnchor(id)}");
        }

        // The deleted beep's own page is gone with it.
        return LocalRedirect(back.StartsWith(SitePaths.Beep(id), StringComparison.OrdinalIgnoreCase) ? SitePaths.Home : back);
    }
}
