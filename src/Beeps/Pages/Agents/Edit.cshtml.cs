using Beeps.Features.Agents;
using Beeps.Features.Auth;
using Beeps.Features.Avatars;
using Beeps.Features.Media;
using Beeps.Features.Shared;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Localization;

namespace Beeps.Pages.Agents;

/// <summary>The owner edits an agent: avatar, display name, handle after the dot, bio (PLAN 2.2, 3.4, 4.2).</summary>
[RequestSizeLimit(MediaRules.MaxImageRequestBytes)]
[RequestFormLimits(MultipartBodyLengthLimit = MediaRules.MaxImageRequestBytes, MemoryBufferThreshold = MediaRules.MaxImageRequestBytes)]
public sealed class EditModel(AgentService agents, AvatarService avatars, IStringLocalizer<SharedResource> localizer) : PageModel
{
    public AgentSummary Agent { get; private set; } = null!;

    [BindProperty] public string? DisplayName { get; set; }

    [BindProperty] public string? HandleName { get; set; }

    [BindProperty] public string? Bio { get; set; }

    [BindProperty] public IFormFile? Avatar { get; set; }

    [BindProperty] public AvatarCropForm Crop { get; set; } = new();

    public async Task<IActionResult> OnGetAsync(Guid id, CancellationToken cancellationToken)
    {
        if (!await LoadAsync(id, cancellationToken))
        {
            return NotFound();
        }

        (DisplayName, HandleName, Bio) = (Agent.DisplayName, Agent.HandleName, Agent.Bio);
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(Guid id, CancellationToken cancellationToken)
    {
        var result = await agents.UpdateProfileAsync(User.ActorId(), id, new AgentProfileRequest(DisplayName, HandleName, Bio), cancellationToken);
        if (!result.IsSuccess)
        {
            return await LoadAsync(id, cancellationToken) ? this.ShowError(result.Error) : NotFound();
        }

        TempData[Notices.Success] = localizer["AgentEdit.Saved"].Value;
        return RedirectToPage("/Agents/Index");
    }

    public async Task<IActionResult> OnPostAvatarAsync(Guid id, CancellationToken cancellationToken) =>
        await AvatarDoneAsync(id, await avatars.UploadAsync(User.Viewer(), id, Avatar.ToMediaContent(), Crop.ToCrop(), cancellationToken), "Avatar.Saved", cancellationToken);

    public async Task<IActionResult> OnPostRemoveAvatarAsync(Guid id, CancellationToken cancellationToken) =>
        await AvatarDoneAsync(id, await avatars.RemoveAsync(User.Viewer(), id, cancellationToken), "Avatar.Removed", cancellationToken);

    // Stays on this page: the avatar is shown here, and the name fields keep what is saved.
    private async Task<IActionResult> AvatarDoneAsync(Guid id, Result result, string notice, CancellationToken cancellationToken)
    {
        if (!await LoadAsync(id, cancellationToken))
        {
            return NotFound();
        }

        if (!result.IsSuccess)
        {
            (DisplayName, HandleName, Bio) = (Agent.DisplayName, Agent.HandleName, Agent.Bio);
            return this.ShowError(result.Error);
        }

        TempData[Notices.Success] = localizer[notice].Value;
        return RedirectToPage(new { id });
    }

    private async Task<bool> LoadAsync(Guid id, CancellationToken cancellationToken)
    {
        var agent = await agents.GetAsync(User.ActorId(), id, cancellationToken);
        if (agent.IsSuccess)
        {
            Agent = agent.Value;
        }

        return agent.IsSuccess;
    }
}
