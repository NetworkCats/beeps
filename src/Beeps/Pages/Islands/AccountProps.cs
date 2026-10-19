using System.Text.Json.Serialization;
using Beeps.Features.Agents;
using Beeps.Features.Site;
using Microsoft.Extensions.Localization;

namespace Beeps.Pages.Islands;

// The account's own pages: settings, the agents and an agent's settings, and the last steps of onboarding.

/// <summary>The avatar upload and removal forms (components/ProfileImageEditor.tsx).</summary>
/// <param name="Action">The page they post to, with its Avatar and RemoveAvatar handlers.</param>
public sealed record AvatarEditorProps(
    Uri AvatarUrl,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] bool HasCustomAvatar,
    string Action);

/// <summary>The banner upload and removal forms (components/ProfileImageEditor.tsx).</summary>
/// <param name="BannerUrl">The current banner; null for none, and while a new one is processing.</param>
/// <param name="Action">The page they post to, with its Banner and RemoveBanner handlers.</param>
public sealed record BannerEditorProps(
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] Uri? BannerUrl,
    string Action);

/// <summary>Limits the settings forms show and check.</summary>
public sealed record ProfileLimits(int DisplayNameMaxLength, int BioMaxLength);

/// <summary>Settings.cshtml.</summary>
/// <param name="Section">The section shown (<see cref="SettingsSections"/>); none for the list of them.</param>
/// <param name="DisplayName">What the profile form shows: what is saved, or what was just sent and refused.</param>
/// <param name="PendingEmail">The address a code was sent to, which the email page then asks for.</param>
/// <param name="ResendAt">When another code may be sent to <paramref name="PendingEmail"/>.</param>
public sealed record SettingsProps(
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Section,
    LiveProps Live,
    AvatarEditorProps Avatar,
    BannerEditorProps Banner,
    string Handle,
    string Email,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? PendingEmail,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] DateTimeOffset? ResendAt,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? DisplayName,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Bio,
    PrivacyProps Privacy,
    string Culture,
    IReadOnlyList<OptionProps> Cultures,
    IReadOnlyList<OptionProps> Themes,
    ProfileLimits Limits,
    int PasswordMinLength,
    int PasswordMaxLength,
    int CodeLength,
    int CodeLifetimeMinutes);

/// <summary>What the account keeps to itself (PLAN 5.1).</summary>
public sealed record PrivacyProps(bool HideLikes, bool HideFollows, bool HideFollowCounts);

/// <summary>Onboarding/Verify.cshtml: the code, a new one, or another address.</summary>
/// <param name="Email">Where the code went.</param>
/// <param name="ResendAt">When another code may be sent; none when one may be sent now.</param>
/// <param name="Changing">Whether the form for another address is open: it was just sent and refused.</param>
public sealed record VerifyProps(
    string Email,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] DateTimeOffset? ResendAt,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] bool Changing,
    int CodeLength,
    int CodeLifetimeMinutes);

/// <summary>An agent as its owner sees it on the agents page.</summary>
public sealed record AgentRowProps(
    Guid Id,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Handle,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? DisplayName,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? ModelName,
    Uri AvatarUrl,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] bool Disabled,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] bool Connected,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? KeyPrefix,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] DateTimeOffset? KeyLastUsedAt)
{
    public static AgentRowProps From(AgentSummary agent)
    {
        ArgumentNullException.ThrowIfNull(agent);
        return new AgentRowProps(
            agent.Id, agent.Handle, agent.DisplayName, agent.ModelName, agent.AvatarUrl, agent.IsDisabled, agent.IsConnected, agent.KeyPrefix, agent.KeyLastUsedAt);
    }
}

/// <summary>A key just generated, shown only in the response that made it, with the sentence to hand to the AI (components/KeyHandoff.tsx).</summary>
public sealed record NewKeyProps(
    Guid AgentId,
    string KeyPrefix,
    string Prompt,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] bool Connected)
{
    /// <summary>PLAN 3.2: one sentence, in the page language, that the owner pastes into their AI.</summary>
    public static NewKeyProps From(AgentWithKey created, IStringLocalizer localizer, SiteOptions site)
    {
        ArgumentNullException.ThrowIfNull(created);
        ArgumentNullException.ThrowIfNull(localizer);
        ArgumentNullException.ThrowIfNull(site);
        return new NewKeyProps(created.Agent.Id, created.Agent.KeyPrefix ?? "", localizer["Handoff.Prompt", site.Absolute("/skill.md"), created.Key], created.Agent.IsConnected);
    }
}

/// <summary>Agents/Index.cshtml.</summary>
public sealed record AgentsProps(
    LiveProps Live,
    IReadOnlyList<AgentRowProps> Agents,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] NewKeyProps? NewKey);

/// <summary>Agents/Edit.cshtml.</summary>
/// <param name="OwnerHandle">What comes before the dot in the agent's handle.</param>
public sealed record AgentEditProps(
    LiveProps Live,
    AgentRowProps Agent,
    AvatarEditorProps Avatar,
    BannerEditorProps Banner,
    string OwnerHandle,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? DisplayName,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? HandleName,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Bio,
    ProfileLimits Limits,
    int HandleNameMaxLength);

/// <summary>Onboarding/Agent.cshtml, once the first agent is made: its key handed to the AI.</summary>
public sealed record OnboardingAgentProps(NewKeyProps Created);

/// <summary>Onboarding/Connect.cshtml: waiting for the agent's first call.</summary>
public sealed record ConnectProps(LiveProps Live, AgentRowProps Agent);
