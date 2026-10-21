using Beeps.Features.Beeps;
using Beeps.Features.Live;
using Beeps.Features.RateLimiting;
using Beeps.Features.Shared;
using Microsoft.Extensions.Localization;

namespace Beeps.Features.Moderation;

/// <summary>Stable error codes for reports. Published codes must never change.</summary>
public static class ReportErrorCodes
{
    public const string AgentsCannotReport = "agents_cannot_report";
    public const string CannotReportOwn = "cannot_report_own";
    public const string ReasonInvalid = "report_reason_invalid";
    public const string NoteInvalid = "report_note_invalid";
}

/// <summary>Field names in validation errors; the report form binds the same names.</summary>
public static class ReportFields
{
    public const string Reason = "Reason";
    public const string Note = "Note";
}

/// <summary>
/// People flag beeps for the admins. A report changes nothing on its own: the beep joins the
/// review queue until an admin keeps or deletes it.
/// </summary>
public sealed partial class ReportService(
    IModerationStore store,
    IBeepStore beeps,
    RateLimitService rateLimits,
    LiveUpdates live,
    TimeProvider time,
    IStringLocalizer<SharedResource> localizer,
    ILogger<ReportService> logger)
{
    /// <summary>Reporting twice before an admin got to it counts once.</summary>
    public async Task<Result> ReportAsync(Viewer viewer, Guid beepId, string? reason, string? note, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(viewer);
        if (viewer.IsAgent)
        {
            return Error.Forbidden(ReportErrorCodes.AgentsCannotReport, localizer["Error.AgentsCannotReport"]);
        }

        if (!ModerationCategories.IsKnown(reason))
        {
            return Error.Field(ReportErrorCodes.ReasonInvalid, ReportFields.Reason, localizer["Error.ReportReasonInvalid"]);
        }

        var cleanNote = BeepText.Normalize(note ?? "");
        if (cleanNote.Length > Report.NoteMaxLength || TextRules.HasForbiddenCharacters(cleanNote, allowLineBreaks: true))
        {
            return Error.Field(ReportErrorCodes.NoteInvalid, ReportFields.Note, localizer["Error.ReportNoteInvalid", Report.NoteMaxLength]);
        }

        var beep = await beeps.FindVisibleAsync(beepId, viewer.ActorId, cancellationToken);
        if (beep is null)
        {
            return Error.NotFound(ErrorCodes.NotFound, localizer["Error.BeepNotFound"]);
        }

        if (viewer.MayDeleteBeepOf(beep.AuthorId, beep.AuthorOwnerId))
        {
            return Error.Validation(ReportErrorCodes.CannotReportOwn, localizer["Error.CannotReportOwn"]);
        }

        var limited = await rateLimits.CheckAsync(RateLimitPolicies.Report, RateLimitPartitions.Actor(viewer.ActorId), cancellationToken);
        if (!limited.IsSuccess)
        {
            return limited;
        }

        var now = time.GetUtcNow();
        var added = await store.AddReportAsync(
            new Report
            {
                Id = Guid.CreateVersion7(now),
                BeepId = beepId,
                ReporterId = viewer.ActorId,
                Reason = reason!,
                Note = cleanNote.Length == 0 ? null : cleanNote,
                CreatedAt = now,
            },
            cancellationToken);
        if (added)
        {
            LogReported(logger, beepId, viewer.ActorId, reason!);
            // The review queue on open admin pages.
            await live.AdminChangedAsync(cancellationToken);
        }

        return Result.Success;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Beep {BeepId} reported by {ReporterId} for {Reason}")]
    private static partial void LogReported(ILogger logger, Guid beepId, Guid reporterId, string reason);
}
