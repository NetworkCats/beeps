using Beeps.Features.Auth;
using Beeps.Features.Beeps;
using Beeps.Features.Media;
using Beeps.Features.Site;
using Beeps.Pages.Islands;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Localization;

namespace Beeps.Pages;

/// <summary>
/// Posts a beep or a reply. The composers on other pages post here; a failed post comes back to this page
/// with the text kept, a successful one goes back to where it was written, at the new beep.
/// </summary>
[RequestSizeLimit(MediaRules.MaxBeepRequestBytes)]
[RequestFormLimits(MultipartBodyLengthLimit = MediaRules.MaxBeepRequestBytes, MemoryBufferThreshold = MediaRules.MaxBeepRequestBytes)]
[AppShell]
public sealed class ComposeModel(BeepService beeps, IStringLocalizer<SharedResource> localizer) : PageModel
{
    [BindProperty(SupportsGet = true)] public Guid? ReplyTo { get; set; }

    [BindProperty(SupportsGet = true)] public string? ReturnUrl { get; set; }

    [BindProperty] public string? Text { get; set; }

    [BindProperty] public IReadOnlyList<IFormFile> Images { get; set; } = [];

    /// <summary>The beep being answered, shown above the form.</summary>
    public BeepView? Parent { get; private set; }

    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken) =>
        await LoadParentAsync(cancellationToken) ? Page() : NotFound();

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        var result = await beeps.PostWithUploadsAsync(User.Viewer(), Text, ReplyTo, Images.ToMediaContents(), cancellationToken);
        if (!result.IsSuccess)
        {
            await LoadParentAsync(cancellationToken);
            return this.ShowError(result.Error);
        }

        TempData[Notices.Success] = localizer[ReplyTo is null ? "Compose.Posted" : "Compose.Replied"].Value;
        var back = Url.IsLocalUrl(ReturnUrl) ? ReturnUrl : ReplyTo is { } parent ? SitePaths.Beep(parent) : SitePaths.Home;
        return LocalRedirect($"{back}#{SitePaths.BeepAnchor(result.Value.Id)}");
    }

    private async Task<bool> LoadParentAsync(CancellationToken cancellationToken)
    {
        if (ReplyTo is not { } id)
        {
            return true;
        }

        var parent = await beeps.GetAsync(User.Viewer(), id, cancellationToken);
        Parent = parent.IsSuccess ? parent.Value : null;
        return parent.IsSuccess;
    }
}
