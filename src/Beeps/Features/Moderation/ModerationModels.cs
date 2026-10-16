using Beeps.Features.Accounts;
using Beeps.Features.Beeps;
using Beeps.Features.Media;
using Beeps.Features.Shared;

namespace Beeps.Features.Moderation;

/// <summary>What the model is shown: the beep to judge and, for a reply, the beep it answers, as context only.</summary>
public sealed record ModerationInput(string Text, string? ReplyToText);

/// <summary>The model's answer, already checked against the schema, with what the call cost.</summary>
public sealed record ModerationVerdict(
    ModerationDecision Decision,
    IReadOnlyList<string> Categories,
    ModerationSeverity Severity,
    string Reason,
    string Model,
    string? Provider,
    int LatencyMs,
    int? Tokens);

/// <summary>
/// Asks the model about one beep (PLAN 6.20). Every failure (network, timeout, 429, 5xx, an answer that does not
/// fit the schema) throws <see cref="ContentModerationException"/>: the review is retried, never taken as allowed.
/// </summary>
public interface IContentModerator
{
    Task<ModerationVerdict> ModerateAsync(ModerationInput input, CancellationToken cancellationToken);
}

public sealed class ContentModerationException : Exception
{
    public ContentModerationException()
    {
    }

    public ContentModerationException(string message)
        : base(message)
    {
    }

    public ContentModerationException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

/// <summary>Stages a review on the current unit of work, so it commits with the beep it is for (PLAN 12).</summary>
public interface IModerationJobs
{
    void Enqueue(Guid beepId);
}

/// <summary>A beep as the review needs it. <see cref="ReplyToText"/> is null for a top-level beep or a deleted parent.</summary>
public sealed record ModerationSubject(
    Guid BeepId,
    string Text,
    ModerationStatus Status,
    bool Deleted,
    Guid AuthorId,
    Guid? AuthorOwnerId,
    Guid? ReplyToId,
    string? ReplyToText);

/// <summary>A beep in the admins' review queue (PLAN 3.6): hidden by the AI, failed review, or reported.</summary>
public sealed record ReviewQueueRow(
    Guid BeepId,
    string Text,
    ActorRow Author,
    ModerationStatus Status,
    DateTimeOffset CreatedAt,
    int OpenReports,
    ModerationResultRow? LatestResult);

public sealed record ModerationResultRow(
    Guid Id,
    string Model,
    string? Provider,
    ModerationDecision Decision,
    IReadOnlyList<string> Categories,
    ModerationSeverity Severity,
    string Reason,
    int LatencyMs,
    int? Tokens,
    DateTimeOffset CreatedAt);

public sealed record ReportRow(
    Guid Id,
    Guid ReporterId,
    string? ReporterHandle,
    string Reason,
    string? Note,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ResolvedAt);

/// <summary>Everything an admin needs to decide on one beep, whatever state it is in (deleted ones too).</summary>
/// <param name="ReplyTo">The beep it answers, for context.</param>
/// <param name="Media">Every attached image, also removed ones.</param>
public sealed record ReviewBeepRow(
    Guid Id,
    string Text,
    ActorRow Author,
    string? AuthorOwnerHandle,
    Guid RootId,
    ModerationStatus Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset? DeletedAt,
    ReviewContextRow? ReplyTo,
    IReadOnlyList<MediaRow> Media,
    IReadOnlyList<ModerationResultRow> Results,
    IReadOnlyList<ReportRow> Reports);

public sealed record ReviewContextRow(Guid Id, string Text, ActorRow Author, DateTimeOffset? DeletedAt);

/// <summary>
/// Moderation data access: the AI review, reports and the admins' review queue. Staged writes are committed by
/// <see cref="IUnitOfWork"/>; methods documented as "runs immediately" execute their SQL at once.
/// </summary>
public interface IModerationStore
{
    /// <summary>Any beep, deleted or not; null if there is no such beep.</summary>
    Task<ModerationSubject?> GetSubjectAsync(Guid beepId, CancellationToken cancellationToken);

    void AddResult(ModerationResult result);

    /// <summary>
    /// Moves the beep to <paramref name="to"/> only if it is in one of <paramref name="from"/> and not deleted, so
    /// whichever decision lands second (the model's or an admin's) sees the first. Runs immediately.
    /// </summary>
    Task<bool> SetStatusAsync(Guid beepId, IReadOnlyCollection<ModerationStatus> from, ModerationStatus to, CancellationToken cancellationToken);

    /// <summary>Removes the beep's pending review, if any. Runs immediately.</summary>
    Task DeleteJobAsync(Guid beepId, CancellationToken cancellationToken);

    /// <summary>Records the report unless the reporter already has an open one on this beep. Runs immediately; false then.</summary>
    Task<bool> AddReportAsync(Report report, CancellationToken cancellationToken);

    /// <summary>Closes the beep's open reports. Runs immediately; returns how many.</summary>
    Task<int> ResolveReportsAsync(Guid beepId, Guid adminId, DateTimeOffset now, CancellationToken cancellationToken);

    /// <summary>Oldest first: beeps not deleted that are hidden, failed review, or have open reports.</summary>
    Task<CursorPage<ReviewQueueRow>> ListQueueAsync(Guid? after, int limit, CancellationToken cancellationToken);

    Task<int> CountQueueAsync(CancellationToken cancellationToken);

    Task<ReviewBeepRow?> GetReviewBeepAsync(Guid beepId, CancellationToken cancellationToken);

    /// <summary>
    /// Takes the beep's images down for good (status removed, pending originals dropped). Runs immediately;
    /// returns the storage keys of those that were stored, for deleting their objects.
    /// </summary>
    Task<IReadOnlyList<string>> RemoveMediaAsync(Guid beepId, CancellationToken cancellationToken);
}
