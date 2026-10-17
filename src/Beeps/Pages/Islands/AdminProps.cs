using System.Globalization;
using System.Text.Json.Serialization;
using Beeps.Features.Accounts;
using Beeps.Features.Admin;
using Beeps.Features.Agents;
using Beeps.Features.Media;
using Beeps.Features.Moderation;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;

namespace Beeps.Pages.Islands;

// The admin pages (Pages/Admin). Every one follows the site as a whole: it is fetched again, now and then, after any
// change, since its figures and lists are about everything (Client/app/live.ts). Texts that come from data the client
// has no fixed list of (moderation categories, audit actions) are localized here.

public static class AdminLive
{
    /// <summary>What every admin page follows: the admin topic, and any change on the site.</summary>
    public static readonly LiveProps Props = new(Watch: "admin", On: "admin");
}

/// <summary>Admin/Index.cshtml: how big the site is, and what waits for attention.</summary>
/// <param name="Since">Start of "today", as <see cref="AdminOverview.Since"/>.</param>
public sealed record AdminOverviewProps(
    LiveProps Live,
    DateTimeOffset Since,
    int Users,
    int Agents,
    int Beeps,
    int NewUsers,
    int NewAgents,
    int NewBeeps,
    int ReviewQueue,
    int ModerationBacklog,
    int OutboxBacklog,
    int DeadJobs)
{
    public static AdminOverviewProps From(AdminOverview o)
    {
        ArgumentNullException.ThrowIfNull(o);
        return new AdminOverviewProps(
            AdminLive.Props, o.Since, o.Users, o.Agents, o.Beeps, o.NewUsers, o.NewAgents, o.NewBeeps, o.ReviewQueue, o.ModerationBacklog, o.OutboxBacklog, o.DeadJobs);
    }
}

/// <summary>A person as the admin pages list them, with the badges shown next to them.</summary>
public sealed record AdminUserProps(
    Guid Id,
    string Handle,
    string DisplayName,
    string Email,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] bool Admin,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] bool EmailVerified,
    DateTimeOffset CreatedAt,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] DateTimeOffset? BannedAt,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] DateTimeOffset? DeletedAt)
{
    public static AdminUserProps From(AdminUserRow user)
    {
        ArgumentNullException.ThrowIfNull(user);
        return new AdminUserProps(user.Id, user.Handle, user.DisplayName, user.Email, user.IsAdmin, user.EmailVerified, user.CreatedAt, user.BannedAt, user.DeletedAt);
    }
}

/// <summary>Admin/Users/Index.cshtml.</summary>
/// <param name="Q">The search, as typed.</param>
public sealed record AdminUsersProps(
    LiveProps Live,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Q,
    PageProps<AdminUserProps> Users);

/// <summary>An agent as an admin sees it on its owner's page.</summary>
/// <param name="HasKey">It has a live key, which the admin can revoke.</param>
public sealed record AdminOwnedAgentProps(
    Guid Id,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Handle,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? DisplayName,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? ModelName,
    Uri AvatarUrl,
    DateTimeOffset CreatedAt,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] bool Disabled,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] bool HasKey)
{
    public static AdminOwnedAgentProps From(AgentSummary agent)
    {
        ArgumentNullException.ThrowIfNull(agent);
        return new AdminOwnedAgentProps(agent.Id, agent.Handle, agent.DisplayName, agent.ModelName, agent.AvatarUrl, agent.CreatedAt, agent.IsDisabled, agent.KeyPrefix is not null);
    }
}

/// <summary>Admin/Users/Details.cshtml.</summary>
/// <param name="Self">The admin looking at their own account: they can only give up being an admin.</param>
/// <param name="ReturnUrl">Where the agents' buttons (which post to the agents page) come back to.</param>
public sealed record AdminUserDetailsProps(
    LiveProps Live,
    AdminUserProps User,
    Uri AvatarUrl,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] bool Self,
    IReadOnlyList<AdminOwnedAgentProps> Agents,
    string ReturnUrl);

/// <summary>An agent in the admins' list of every agent.</summary>
public sealed record AdminAgentRowProps(
    Guid Id,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Handle,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? ModelName,
    Guid OwnerId,
    string OwnerHandle,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] bool Disabled,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] bool Deleted,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? KeyPrefix)
{
    public static AdminAgentRowProps From(AdminAgentRow agent)
    {
        ArgumentNullException.ThrowIfNull(agent);
        return new AdminAgentRowProps(
            agent.Id, agent.Handle, agent.ModelName, agent.OwnerId, agent.OwnerHandle, agent.DisabledAt is not null, agent.DeletedAt is not null, agent.KeyPrefix);
    }
}

/// <summary>Admin/Agents/Index.cshtml.</summary>
/// <param name="ReturnUrl">This page (with its search), where the agents' buttons come back to.</param>
public sealed record AdminAgentsProps(
    LiveProps Live,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Q,
    PageProps<AdminAgentRowProps> Agents,
    string ReturnUrl);

/// <summary>A beep in the review queue.</summary>
/// <param name="Status">Its <see cref="Features.Beeps.ModerationStatus"/>, by name.</param>
/// <param name="Categories">What the AI hid it for, when it did, localized.</param>
public sealed record ReviewQueueRowProps(
    Guid BeepId,
    string Text,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? AuthorHandle,
    string Status,
    DateTimeOffset CreatedAt,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] int OpenReports,
    IReadOnlyList<string> Categories)
{
    public static ReviewQueueRowProps From(ReviewQueueRow item, IStringLocalizer localizer)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(localizer);
        IReadOnlyList<string> categories = item.LatestResult is { Decision: ModerationDecision.Hide } result
            ? [.. result.Categories.Select(c => localizer[$"Moderation.Category.{c}"].Value)]
            : [];
        return new ReviewQueueRowProps(item.BeepId, item.Text, item.Author.Handle, item.Status.ToString(), item.CreatedAt, item.OpenReports, categories);
    }
}

/// <summary>Admin/Review/Index.cshtml.</summary>
public sealed record AdminReviewProps(LiveProps Live, PageProps<ReviewQueueRowProps> Queue);

/// <summary>The beep a reviewed beep answers.</summary>
public sealed record ReviewContextProps(
    Guid Id,
    string Text,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? AuthorHandle,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] bool Deleted);

/// <summary>An image of a reviewed beep: its addresses once it is ready, its <see cref="MediaStatus"/> by name otherwise.</summary>
public sealed record ReviewMediaProps(
    string Status,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] Uri? Url,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] Uri? Thumbnail,
    int Width,
    int Height)
{
    public static ReviewMediaProps From(MediaRow media, MediaUrls urls)
    {
        ArgumentNullException.ThrowIfNull(media);
        ArgumentNullException.ThrowIfNull(urls);
        var ready = media.Status == MediaStatus.Ready;
        return new ReviewMediaProps(
            media.Status.ToString(), ready ? urls.Full(media.StorageKey) : null, ready ? urls.Thumbnail(media.StorageKey) : null, media.Width, media.Height);
    }
}

/// <summary>A report on a reviewed beep.</summary>
/// <param name="Reason">Localized.</param>
public sealed record ReviewReportProps(
    DateTimeOffset CreatedAt,
    Guid ReporterId,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? ReporterHandle,
    string Reason,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Note,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] bool Resolved)
{
    public static ReviewReportProps From(ReportRow report, IStringLocalizer localizer)
    {
        ArgumentNullException.ThrowIfNull(report);
        ArgumentNullException.ThrowIfNull(localizer);
        return new ReviewReportProps(
            report.CreatedAt, report.ReporterId, report.ReporterHandle, localizer[$"Moderation.Category.{report.Reason}"], report.Note, report.ResolvedAt is not null);
    }
}

/// <summary>One of the AI's answers on a reviewed beep.</summary>
/// <param name="Decision">Its <see cref="ModerationDecision"/>, by name.</param>
/// <param name="Severity">Its <see cref="ModerationSeverity"/>, by name.</param>
/// <param name="Categories">Localized.</param>
/// <param name="Details">The provider, latency and tokens, as one localized line.</param>
public sealed record ReviewResultProps(
    DateTimeOffset CreatedAt,
    string Decision,
    string Severity,
    IReadOnlyList<string> Categories,
    string Reason,
    string Model,
    string Details)
{
    public static ReviewResultProps From(ModerationResultRow result, IStringLocalizer localizer)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(localizer);
        var tokens = result.Tokens?.ToString(CultureInfo.InvariantCulture) ?? "—";
        var details = $"{result.Provider ?? "—"} · {localizer["Admin.Beep.Latency", result.LatencyMs]} · {localizer["Admin.Beep.Tokens", tokens]}";
        return new ReviewResultProps(
            result.CreatedAt,
            result.Decision.ToString(),
            result.Severity.ToString(),
            [.. result.Categories.Select(c => localizer[$"Moderation.Category.{c}"].Value)],
            result.Reason,
            result.Model,
            details);
    }
}

/// <summary>Admin/Review/Beep.cshtml: one beep as an admin reviews it.</summary>
/// <param name="AccountId">The account that wrote it: the owner's, for an agent.</param>
/// <param name="Status">Its <see cref="Features.Beeps.ModerationStatus"/>, by name.</param>
/// <param name="Keepable">There is a decision to make: it is not kept by an admin already, or a report came since.</param>
/// <param name="MediaShown">Some of its images are not taken down yet.</param>
public sealed record AdminReviewBeepProps(
    LiveProps Live,
    Guid Id,
    string Text,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? AuthorHandle,
    Guid AccountId,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? AuthorOwnerHandle,
    string Status,
    DateTimeOffset CreatedAt,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] DateTimeOffset? DeletedAt,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] ReviewContextProps? ReplyTo,
    IReadOnlyList<ReviewMediaProps> Media,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] bool Keepable,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] bool MediaShown,
    IReadOnlyList<ReviewReportProps> Reports,
    IReadOnlyList<ReviewResultProps> Results)
{
    public static AdminReviewBeepProps From(ReviewBeepRow beep, MediaUrls urls, IStringLocalizer localizer)
    {
        ArgumentNullException.ThrowIfNull(beep);
        ArgumentNullException.ThrowIfNull(urls);
        ArgumentNullException.ThrowIfNull(localizer);
        // An admin's earlier decision with no report since leaves nothing to handle.
        var keepable = beep.Status != Features.Beeps.ModerationStatus.HumanAllowed || beep.Reports.Any(r => r.ResolvedAt is null);
        return new AdminReviewBeepProps(
            AdminLive.Props,
            beep.Id,
            beep.Text,
            beep.Author.Handle,
            beep.Author.OwnerId ?? beep.Author.Id,
            beep.AuthorOwnerHandle,
            beep.Status.ToString(),
            beep.CreatedAt,
            beep.DeletedAt,
            beep.ReplyTo is { } parent ? new ReviewContextProps(parent.Id, parent.Text, parent.Author.Handle, parent.DeletedAt is not null) : null,
            [.. beep.Media.Select(m => ReviewMediaProps.From(m, urls))],
            keepable,
            beep.Media.Any(m => m.Status != MediaStatus.Removed),
            [.. beep.Reports.Select(r => ReviewReportProps.From(r, localizer))],
            [.. beep.Results.Select(r => ReviewResultProps.From(r, localizer))]);
    }
}

public sealed record DeadJobProps(Guid Id, string Type, int Attempts, string LastError, DateTimeOffset FailedAt)
{
    public static DeadJobProps From(DeadJobRow job)
    {
        ArgumentNullException.ThrowIfNull(job);
        return new DeadJobProps(job.Id, job.Type, job.Attempts, job.LastError, job.FailedAt);
    }
}

public sealed record ScheduledJobProps(
    string Name,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] DateTimeOffset? LastStartedAt,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] DateTimeOffset? LastFinishedAt,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? LastError)
{
    public static ScheduledJobProps From(ScheduledJobRow job)
    {
        ArgumentNullException.ThrowIfNull(job);
        return new ScheduledJobProps(job.Name, job.LastStartedAt, job.LastFinishedAt, job.LastError);
    }
}

/// <summary>Admin/System.cshtml.</summary>
public sealed record AdminSystemProps(LiveProps Live, PageProps<DeadJobProps> DeadJobs, IReadOnlyList<ScheduledJobProps> Scheduled);

/// <summary>A registration mode to choose, with its label and help localized.</summary>
public sealed record RegistrationModeProps(string Value, string Label, string Help);

public sealed record InviteProps(
    Guid Id,
    string CodePrefix,
    DateTimeOffset CreatedAt,
    DateTimeOffset ExpiresAt,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] bool Used,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? UsedByHandle)
{
    public static InviteProps From(InviteRow invite)
    {
        ArgumentNullException.ThrowIfNull(invite);
        return new InviteProps(invite.Id, invite.CodePrefix, invite.CreatedAt, invite.ExpiresAt, invite.UsedAt is not null, invite.UsedByHandle);
    }
}

/// <summary>An invite just created, with its full code: shown only in the response that created it.</summary>
public sealed record NewInviteProps(Guid Id, string Code);

/// <summary>Admin/Settings.cshtml.</summary>
public sealed record AdminSettingsProps(
    LiveProps Live,
    string Mode,
    IReadOnlyList<RegistrationModeProps> Modes,
    IReadOnlyList<InviteProps> Invites,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] NewInviteProps? NewInvite)
{
    public static IReadOnlyList<RegistrationModeProps> ModesFor(IStringLocalizer localizer)
    {
        ArgumentNullException.ThrowIfNull(localizer);
        return [.. Enum.GetValues<RegistrationMode>().Select(m => new RegistrationModeProps(m.ToString(), localizer[$"Admin.Registration.{m}"], localizer[$"Admin.Registration.{m}.Help"]))];
    }
}

/// <summary>An entry of the audit log.</summary>
/// <param name="Action">Localized.</param>
/// <param name="Target">What it was done to, localized.</param>
/// <param name="TargetUrl">The admin page about that, when there is one.</param>
public sealed record AuditEntryProps(
    Guid Id,
    DateTimeOffset CreatedAt,
    Guid AdminId,
    string AdminHandle,
    string Action,
    string Target,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? TargetUrl,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Detail)
{
    public static AuditEntryProps From(AuditLogRow entry, IStringLocalizer localizer, IUrlHelper url)
    {
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentNullException.ThrowIfNull(localizer);
        ArgumentNullException.ThrowIfNull(url);
        string? UserPage(Guid? id) => url.Page("/Admin/Users/Details", new { id });
        var (target, targetUrl) = entry.TargetType switch
        {
            AuditTargets.User when entry.TargetHandle is not null => ($"@{entry.TargetHandle}", UserPage(entry.TargetId)),
            // An agent is listed on its owner's page.
            AuditTargets.Agent when entry.TargetOwnerId is not null =>
                (entry.TargetHandle is null ? localizer["Audit.Target.agent"].Value : $"@{entry.TargetHandle}", UserPage(entry.TargetOwnerId)),
            AuditTargets.Beep when entry.TargetId is not null => (localizer["Audit.Target.beep"].Value, url.Page("/Admin/Review/Beep", new { id = entry.TargetId })),
            _ => (localizer[$"Audit.Target.{entry.TargetType}"].Value, null),
        };
        return new AuditEntryProps(
            entry.Id, entry.CreatedAt, entry.AdminId, entry.AdminHandle, localizer[$"Audit.{entry.Action}"], target, targetUrl, entry.Detail);
    }
}

/// <summary>Admin/AuditLog.cshtml.</summary>
public sealed record AdminAuditProps(LiveProps Live, PageProps<AuditEntryProps> Entries);
