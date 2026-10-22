using Beeps.Features.Beeps;
using Beeps.Features.Live;
using Beeps.Features.Media;
using Beeps.Features.Moderation;
using Beeps.Features.Notifications;
using Beeps.Features.Search;
using Beeps.Features.Shared;
using Beeps.Features.Translation;
using Microsoft.Extensions.Localization;

namespace Beeps.Features.Admin;

/// <summary>
/// The admins' review queue: beeps the AI hid, beeps whose review failed, and reported beeps.
/// An admin keeps a beep (restoring it if hidden) or deletes it, and can take its images down. A person's decision
/// is final: the AI never reviews that beep again.
/// </summary>
public sealed partial class ReviewService(
    IModerationStore store,
    IBeepStore beepStore,
    BeepService beeps,
    AdminAccess access,
    IUnitOfWork unitOfWork,
    IOutbox outbox,
    ITranslationJobs translationJobs,
    LiveUpdates live,
    InboxRecounter inboxes,
    TimeProvider time,
    IStringLocalizer<SharedResource> localizer,
    ILogger<ReviewService> logger)
{
    public const int PageSize = 50;

    /// <summary>Any state a beep can be kept from; keeping it makes it <see cref="ModerationStatus.HumanAllowed"/>.</summary>
    private static readonly ModerationStatus[] Keepable = [ModerationStatus.Pending, ModerationStatus.Allowed, ModerationStatus.Hidden, ModerationStatus.Failed];

    public async Task<Result<CursorPage<ReviewQueueRow>>> ListQueueAsync(Guid adminId, Guid? after, CancellationToken cancellationToken) =>
        await access.RequireAdminAsync(adminId, cancellationToken) is { } denied
            ? denied
            : await store.ListQueueAsync(after, PageSize, cancellationToken);

    public async Task<Result<ReviewBeepRow>> GetBeepAsync(Guid adminId, Guid beepId, CancellationToken cancellationToken)
    {
        if (await access.RequireAdminAsync(adminId, cancellationToken) is { } denied)
        {
            return denied;
        }

        return await store.GetReviewBeepAsync(beepId, cancellationToken) is { } beep ? beep : NotFound();
    }

    /// <summary>
    /// "Restore" for a hidden beep, "mark handled" for the others: the beep is visible, reviewed by a
    /// person, and the open reports the admin saw are closed.
    /// </summary>
    /// <param name="seenReports">
    /// The open reports the admin's page showed: one filed since is not closed unread, and keeps the beep in the queue.
    /// </param>
    public async Task<Result> KeepAsync(Guid adminId, Guid beepId, IReadOnlyCollection<Guid> seenReports, CancellationToken cancellationToken)
    {
        if (await access.RequireAdminAsync(adminId, cancellationToken) is { } denied)
        {
            return denied;
        }

        var subject = await store.GetSubjectAsync(beepId, cancellationToken);
        if (subject is not { Deleted: false })
        {
            return NotFound();
        }

        var (kept, restored) = (false, false);
        var result = await unitOfWork.InTransactionAsync(
            null,
            async ct =>
            {
                // The status now, not the one read above: the model may have decided in between.
                var before = await store.LockStatusAsync(beepId, ct) ?? subject.Status;
                var changed = await store.SetStatusAsync(beepId, Keepable, ModerationStatus.HumanAllowed, ct);
                var resolved = seenReports.Count == 0 ? 0 : await store.ResolveReportsAsync(beepId, seenReports, adminId, time.GetUtcNow(), ct);
                if (!changed && resolved == 0)
                {
                    return Result.Success;
                }

                await store.DeleteJobAsync(beepId, ct);
                if (changed)
                {
                    // Back in search if it was hidden; harmless if it was not.
                    outbox.SyncBeepToSearch(beepId);
                }

                // Let through only now (the model's review never did, or had not yet): translated from now on.
                if (changed && before != ModerationStatus.Allowed && subject.Text.Length > 0)
                {
                    translationJobs.Enqueue(beepId);
                }

                kept = true;
                restored = changed && before == ModerationStatus.Hidden;
                access.Audit(adminId, AuditActions.KeepBeep, AuditTargets.Beep, beepId, Detail(before, changed ? ModerationStatus.HumanAllowed : before, resolved));
                await unitOfWork.SaveChangesAsync(ct);
                return Result.Success;
            },
            cancellationToken);
        if (!kept)
        {
            return result;
        }

        LogKept(logger, beepId, adminId);
        if (restored)
        {
            // Back for everyone: pages that list it fetch it again, its author's loses the "under review" mark.
            await live.BeepsChangedAsync([beepId], cancellationToken);
            await live.ResyncAsync(cancellationToken);
            await inboxes.RecountAsync([beepId], [], cancellationToken);
            if (subject.ReplyToId is { } parentId)
            {
                await beeps.ReplyVisibilityChangedAsync(parentId, visible: true, cancellationToken);
            }
        }

        return result;
    }

    /// <summary>Deletes the beep for everyone; the record stays for audit.</summary>
    public async Task<Result> DeleteAsync(Guid adminId, Guid beepId, CancellationToken cancellationToken)
    {
        if (await access.RequireAdminAsync(adminId, cancellationToken) is { } denied)
        {
            return denied;
        }

        var subject = await store.GetSubjectAsync(beepId, cancellationToken);
        if (subject is null)
        {
            return NotFound();
        }

        ModerationStatus? deleted = null;
        var result = await unitOfWork.InTransactionAsync(
            null,
            async ct =>
            {
                var now = time.GetUtcNow();
                deleted = await beepStore.DeleteAsync(beepId, now, ct);
                if (deleted is not { } status)
                {
                    return Result.Success;
                }

                var resolved = await store.ResolveReportsAsync(beepId, adminId, now, ct);
                await store.DeleteJobAsync(beepId, ct);
                outbox.SyncBeepToSearch(beepId);
                access.Audit(adminId, AuditActions.DeleteBeep, AuditTargets.Beep, beepId, Detail(status, status, resolved));
                await unitOfWork.SaveChangesAsync(ct);
                return Result.Success;
            },
            cancellationToken);
        if (deleted is null)
        {
            return result;
        }

        LogDeleted(logger, beepId, adminId);
        // Deleted already: what follows is done even if the request is given up on now (BeepService.RemoveAsync).
        await live.BeepRemovedAsync(beepId, CancellationToken.None);
        await inboxes.RecountAsync([beepId], [], CancellationToken.None);
        // A hidden reply was already left out of its parent's count.
        if (subject.ReplyToId is { } parentId && deleted != ModerationStatus.Hidden)
        {
            await beeps.ReplyVisibilityChangedAsync(parentId, visible: false, CancellationToken.None);
        }

        return result;
    }

    /// <summary>
    /// Takes the beep's images down for good: never shown again, their stored objects deleted. The
    /// beep itself stays as it is.
    /// </summary>
    public async Task<Result> RemoveMediaAsync(Guid adminId, Guid beepId, CancellationToken cancellationToken)
    {
        if (await access.RequireAdminAsync(adminId, cancellationToken) is { } denied)
        {
            return denied;
        }

        if (await store.GetSubjectAsync(beepId, cancellationToken) is null)
        {
            return NotFound();
        }

        var removed = 0;
        var result = await unitOfWork.InTransactionAsync(
            null,
            async ct =>
            {
                var keys = await store.RemoveMediaAsync(beepId, ct);
                removed = keys.Count;
                if (removed == 0)
                {
                    return Result.Success;
                }

                outbox.Enqueue(DeleteMediaObjectsRequest.MessageType, new DeleteMediaObjectsRequest(keys), MediaJsonContext.Default.DeleteMediaObjectsRequest);
                access.Audit(adminId, AuditActions.RemoveBeepMedia, AuditTargets.Beep, beepId, $"images: {removed}");
                await unitOfWork.SaveChangesAsync(ct);
                return Result.Success;
            },
            cancellationToken);
        if (removed > 0)
        {
            LogMediaRemoved(logger, removed, beepId, adminId);
            await live.BeepsChangedAsync([beepId], cancellationToken);
        }

        return result;
    }

    private static string Detail(ModerationStatus from, ModerationStatus to, int reportsResolved) =>
        from == to ? $"{from}, reports resolved: {reportsResolved}" : $"{from} -> {to}, reports resolved: {reportsResolved}";

    private Error NotFound() => Error.NotFound(ErrorCodes.NotFound, localizer["Error.BeepNotFound"]);

    [LoggerMessage(Level = LogLevel.Information, Message = "Beep {BeepId} kept by admin {AdminId}")]
    private static partial void LogKept(ILogger logger, Guid beepId, Guid adminId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Beep {BeepId} deleted by admin {AdminId}")]
    private static partial void LogDeleted(ILogger logger, Guid beepId, Guid adminId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "{Count} images of beep {BeepId} removed by admin {AdminId}")]
    private static partial void LogMediaRemoved(ILogger logger, int count, Guid beepId, Guid adminId);
}
