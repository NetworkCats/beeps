using Beeps.Features.Auth;
using Beeps.Features.Beeps;
using Beeps.Features.Moderation;
using Beeps.Features.Site;
using Beeps.Pages.Islands;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Localization;

namespace Beeps.Pages.Beep;

/// <summary>Reporting a beep to the admins (PLAN 3.6): a reason and an optional note.</summary>
[AppShell]
public sealed class ReportModel(BeepService beeps, ReportService reports, IStringLocalizer<SharedResource> localizer) : PageModel
{
    /// <summary>What people can pick; "sexual_minors" is left to "sexual" and the admins, as most sites do.</summary>
    public static readonly IReadOnlyList<string> Reasons = [.. ModerationCategories.All.Where(c => c != ModerationCategories.SexualMinors)];

    [BindProperty] public string? Reason { get; set; }

    [BindProperty] public string? Note { get; set; }

    public BeepView Beep { get; private set; } = null!;

    public async Task<IActionResult> OnGetAsync(Guid id, CancellationToken cancellationToken) =>
        await LoadAsync(id, cancellationToken) ? Page() : NotFound();

    public async Task<IActionResult> OnPostAsync(Guid id, CancellationToken cancellationToken)
    {
        var result = await reports.ReportAsync(User.Viewer(), id, Reason, Note, cancellationToken);
        if (!result.IsSuccess)
        {
            return await LoadAsync(id, cancellationToken) ? this.ShowError(result.Error) : NotFound();
        }

        TempData[Notices.Success] = localizer["Report.Sent"].Value;
        return LocalRedirect(SitePaths.Beep(id));
    }

    private async Task<bool> LoadAsync(Guid id, CancellationToken cancellationToken)
    {
        var beep = await beeps.GetAsync(User.Viewer(), id, cancellationToken);
        if (!beep.IsSuccess)
        {
            return false;
        }

        Beep = beep.Value;
        return true;
    }
}
