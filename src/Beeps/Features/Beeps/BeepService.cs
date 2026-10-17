using Beeps.Features.Live;
using Beeps.Features.Media;
using Beeps.Features.Moderation;
using Beeps.Features.Notifications;
using Beeps.Features.Profiles;
using Beeps.Features.RateLimiting;
using Beeps.Features.Search;
using Beeps.Features.Shared;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Options;

namespace Beeps.Features.Beeps;

/// <summary>Posting, replying, deleting and liking (PLAN 2.2, 6.6, 6.19), for humans and agents alike; and what humans view.</summary>
public sealed partial class BeepService(
    IBeepStore store,
    IBeepCounterStore counters,
    IMediaStore mediaStore,
    MediaService media,
    BeepViews views,
    IUnitOfWork unitOfWork,
    IOutbox outbox,
    IModerationJobs moderationJobs,
    RateLimitService rateLimits,
    LiveUpdates live,
    InboxRecounter inboxes,
    IOptions<ModerationOptions> moderation,
    TimeProvider time,
    IStringLocalizer<SharedResource> localizer,
    ILogger<BeepService> logger)
{
    /// <summary>A beep opened again this soon after the view last counted is the same view (gone back to, reloaded).</summary>
    public static readonly TimeSpan RepeatViewWindow = TimeSpan.FromMinutes(30);

    /// <summary>
    /// Posts a beep, or a reply when <paramref name="replyToId"/> is set, with up to four images the author
    /// uploaded (<paramref name="mediaIds"/>; with images the text may be empty). Mentions resolve to actors
    /// that exist right now; hashtags, mentions, images, the notification job and the review (PLAN 6.20)
    /// commit together with the beep. Nobody replies to someone who blocked them.
    /// </summary>
    public async Task<Result<BeepView>> PostAsync(Viewer author, string? text, Guid? replyToId, IReadOnlyList<Guid>? mediaIds, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(author);
        IReadOnlyList<Guid> images = mediaIds is null ? [] : [.. mediaIds.Distinct()];
        if (images.Count > MediaRules.MaxImagesPerBeep)
        {
            return media.TooMany(MediaFields.MediaIds);
        }

        var parsed = BeepText.Parse(text, allowEmpty: images.Count > 0);
        if (!parsed.IsValid)
        {
            return TextError(parsed.Problem);
        }

        BeepRef? parent = null;
        if (replyToId is { } parentId)
        {
            parent = await store.FindVisibleAsync(parentId, author.ActorId, cancellationToken);
            if (parent is null)
            {
                return NotFound();
            }

            if (await BlockedByAuthorAsync(author, parent, cancellationToken))
            {
                return Blocked();
            }
        }

        var limited = await rateLimits.CheckAsync(PostLimits(author, parent), cancellationToken);
        if (!limited.IsSuccess)
        {
            return limited.Error;
        }

        IReadOnlyList<ActorRow> mentioned = parsed.MentionHandles.Count == 0
            ? []
            : await store.FindActorsByHandlesAsync(parsed.MentionHandles, cancellationToken);
        var now = time.GetUtcNow();
        var id = Guid.CreateVersion7(now);
        // Only text is reviewed (PLAN 6.20), so a beep of images alone has nothing to wait for; with moderation
        // off (local development) nothing will ever review it. Neither is left pending.
        var review = moderation.Value.Enabled && parsed.Text.Length > 0;
        var beep = new Beep
        {
            Id = id,
            AuthorId = author.ActorId,
            Text = parsed.Text,
            ReplyToId = parent?.Id,
            RootId = parent?.RootId ?? id,
            ModerationStatus = review ? ModerationStatus.Pending : ModerationStatus.Allowed,
            CreatedAt = now,
        };
        var saved = await unitOfWork.InTransactionAsync(
            null,
            async ct =>
            {
                // Locked until commit, so the cleanup of unused uploads cannot take them meanwhile.
                if (images.Count > 0 && (await mediaStore.LockOwnedAsync(author.ActorId, images, ct)).Count != images.Count)
                {
                    return media.NotFound(MediaFields.MediaIds);
                }

                IReadOnlyDictionary<string, Guid> hashtags = parsed.Hashtags.Count == 0
                    ? new Dictionary<string, Guid>()
                    : await store.EnsureHashtagsAsync(parsed.Hashtags, now, ct);
                store.AddBeep(beep, [.. mentioned.Select(m => m.Id)], [.. hashtags.Values]);
                if (images.Count > 0)
                {
                    store.AttachMedia(id, images);
                }

                if (parent is not null || mentioned.Count > 0)
                {
                    outbox.Enqueue(BeepNotificationsRequest.MessageType, new BeepNotificationsRequest(id), NotificationsJsonContext.Default.BeepNotificationsRequest);
                }

                outbox.SyncBeepToSearch(id);
                if (review)
                {
                    moderationJobs.Enqueue(id);
                }

                await unitOfWork.SaveChangesAsync(ct);
                return Result.Success;
            },
            cancellationToken);
        if (!saved.IsSuccess)
        {
            return saved.Error;
        }

        if (parent is not null)
        {
            await CountAsync(parent, new BeepCounts(0, 1), cancellationToken);
        }

        LogPosted(logger, id, author.ActorId, parent?.Id, images.Count);
        await live.BeepPostedAsync(
            new BeepPostedEvent(id, author.ActorId, author.AccountId, parent?.Id, parent is null ? null : parent.AuthorOwnerId ?? parent.AuthorId, beep.RootId, parsed.Hashtags),
            cancellationToken);
        return await GetAsync(author, id, cancellationToken);
    }

    /// <summary>
    /// A beep written on the web, with its image files: they are uploaded, then posted with the text. The
    /// text and the number of images are checked first, so a beep that cannot be posted uploads nothing.
    /// </summary>
    public async Task<Result<BeepView>> PostWithUploadsAsync(Viewer author, string? text, Guid? replyToId, IReadOnlyList<MediaContent> files, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(files);
        if (files.Count > MediaRules.MaxImagesPerBeep)
        {
            return media.TooMany(MediaFields.Images);
        }

        var parsed = BeepText.Parse(text, allowEmpty: files.Count > 0);
        if (!parsed.IsValid)
        {
            return TextError(parsed.Problem);
        }

        var uploaded = await media.UploadAllAsync(author, files, cancellationToken);
        return uploaded.IsSuccess ? await PostAsync(author, text, replyToId, uploaded.Value, cancellationToken) : uploaded.Error;
    }

    public async Task<Result<BeepView>> GetAsync(Viewer? viewer, Guid beepId, CancellationToken cancellationToken)
    {
        var rows = await store.GetRowsAsync([beepId], viewer?.ActorId, cancellationToken);
        if (rows.Count == 0)
        {
            return NotFound();
        }

        var built = await views.BuildAsync(rows, viewer, cancellationToken);
        return built[0];
    }

    /// <summary>The ones of these beeps the viewer can see, in the order asked for.</summary>
    public async Task<IReadOnlyList<BeepView>> GetManyAsync(Viewer? viewer, IReadOnlyCollection<Guid> beepIds, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(beepIds);
        if (beepIds.Count == 0)
        {
            return [];
        }

        var rows = await store.GetRowsAsync(beepIds, viewer?.ActorId, cancellationToken);
        var built = (await views.BuildAsync(rows, viewer, cancellationToken)).ToDictionary(b => b.Id);
        return [.. beepIds.Distinct().Where(built.ContainsKey).Select(id => built[id])];
    }

    /// <summary>The beeps of a thread after <paramref name="after"/>, oldest first, by the id of its root (which may itself be deleted).</summary>
    public async Task<CursorPage<BeepView>> ListThreadAsync(Viewer? viewer, Guid rootId, Guid? after, int? limit, CancellationToken cancellationToken)
    {
        var page = await store.ListThreadAsync(rootId, after, PageRequest.ClampLimit(limit), viewer?.ActorId, cancellationToken);
        return await views.BuildPageAsync(page, viewer, cancellationToken);
    }

    /// <summary>The whole thread the beep belongs to, oldest first, from its root.</summary>
    public async Task<Result<CursorPage<BeepView>>> GetThreadAsync(Viewer? viewer, Guid beepId, Guid? after, int? limit, CancellationToken cancellationToken)
    {
        var beep = await store.FindVisibleAsync(beepId, viewer?.ActorId, cancellationToken);
        if (beep is null)
        {
            return NotFound();
        }

        var page = await store.ListThreadAsync(beep.RootId, after, PageRequest.ClampLimit(limit), viewer?.ActorId, cancellationToken);
        return await views.BuildPageAsync(page, viewer, cancellationToken);
    }

    /// <summary>Authors delete their own beeps; a human also deletes their agents' beeps (PLAN 2.2).</summary>
    public async Task<Result> DeleteAsync(Viewer viewer, Guid beepId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(viewer);
        var beep = await store.FindVisibleAsync(beepId, viewer.ActorId, cancellationToken);
        if (beep is null)
        {
            return NotFound();
        }

        if (!viewer.MayDeleteBeepOf(beep.AuthorId, beep.AuthorOwnerId))
        {
            return Error.Forbidden(BeepErrorCodes.NotYourBeep, localizer["Error.NotYourBeep"]);
        }

        var deleted = false;
        await unitOfWork.InTransactionAsync(
            null,
            async ct =>
            {
                deleted = await store.DeleteAsync(beepId, time.GetUtcNow(), ct);
                if (deleted)
                {
                    outbox.SyncBeepToSearch(beepId);
                    await unitOfWork.SaveChangesAsync(ct);
                }

                return Result.Success;
            },
            cancellationToken);
        if (!deleted)
        {
            return Result.Success;
        }

        LogDeleted(logger, beepId, viewer.ActorId);
        await live.BeepRemovedAsync(beepId, cancellationToken);
        await inboxes.RecountAsync([beepId], [], cancellationToken);
        if (beep.ReplyToId is { } parentId && await store.FindVisibleAsync(parentId, viewer.ActorId, cancellationToken) is { } parent)
        {
            await CountAsync(parent, new BeepCounts(0, -1), cancellationToken);
        }

        return Result.Success;
    }

    /// <summary>
    /// Liking twice is the same as liking once; only the first like notifies the author. Nobody likes the beeps
    /// of someone who blocked them.
    /// </summary>
    public async Task<Result> LikeAsync(Viewer viewer, Guid beepId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(viewer);
        var beep = await store.FindVisibleAsync(beepId, viewer.ActorId, cancellationToken);
        if (beep is null)
        {
            return NotFound();
        }

        if (await BlockedByAuthorAsync(viewer, beep, cancellationToken))
        {
            return Blocked();
        }

        var limited = await rateLimits.CheckAsync(RateLimitPolicies.Like, ActorPartition(viewer.ActorId), cancellationToken);
        if (!limited.IsSuccess)
        {
            return limited;
        }

        var added = false;
        await unitOfWork.InTransactionAsync(
            null,
            async ct =>
            {
                added = await store.AddLikeAsync(viewer.ActorId, beepId, time.GetUtcNow(), ct);
                if (added && beep.AuthorId != viewer.ActorId)
                {
                    outbox.Enqueue(LikeNotificationRequest.MessageType, new LikeNotificationRequest(beepId, viewer.ActorId), NotificationsJsonContext.Default.LikeNotificationRequest);
                    await unitOfWork.SaveChangesAsync(ct);
                }

                return Result.Success;
            },
            cancellationToken);
        if (added)
        {
            await CountAsync(beep, new BeepCounts(1, 0), cancellationToken);
            await live.LikeChangedAsync(beepId, viewer.ActorId, liked: true, cancellationToken);
        }

        return Result.Success;
    }

    /// <summary>
    /// A signed-in person opened the beep's page: it goes into their viewing history and the beep's view count, which
    /// no one is shown. Agents read through the API, which counts nothing.
    /// </summary>
    public async Task<Result> RecordViewAsync(Viewer viewer, Guid beepId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(viewer);
        if (viewer.IsAgent)
        {
            return Result.Success;
        }

        if (await store.FindVisibleAsync(beepId, viewer.ActorId, cancellationToken) is null)
        {
            return NotFound();
        }

        var now = time.GetUtcNow();
        await store.RecordViewAsync(viewer.ActorId, beepId, now, now - RepeatViewWindow, cancellationToken);
        return Result.Success;
    }

    public async Task<Result> UnlikeAsync(Viewer viewer, Guid beepId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(viewer);
        var beep = await store.FindVisibleAsync(beepId, viewer.ActorId, cancellationToken);
        if (beep is null)
        {
            return NotFound();
        }

        // Same quota as liking, so flipping a like on and off is not free.
        var limited = await rateLimits.CheckAsync(RateLimitPolicies.Like, ActorPartition(viewer.ActorId), cancellationToken);
        if (!limited.IsSuccess)
        {
            return limited;
        }

        if (await store.RemoveLikeAsync(viewer.ActorId, beepId, cancellationToken))
        {
            await CountAsync(beep, new BeepCounts(-1, 0), cancellationToken);
            await live.LikeChangedAsync(beepId, viewer.ActorId, liked: false, cancellationToken);
        }

        return Result.Success;
    }

    /// <summary>
    /// A reply was hidden by moderation or shown again by an admin: the reply count of the beep it answers
    /// follows, as it does when a reply is posted or deleted.
    /// </summary>
    public async Task ReplyVisibilityChangedAsync(Guid parentId, bool visible, CancellationToken cancellationToken)
    {
        if (await store.FindVisibleAsync(parentId, null, cancellationToken) is { } parent)
        {
            await CountAsync(parent, new BeepCounts(0, visible ? 1 : -1), cancellationToken);
        }
    }

    /// <summary>Whether the human behind the beep's author blocked the actor, or the actor's owner (PLAN 5.1).</summary>
    private async Task<bool> BlockedByAuthorAsync(Viewer actor, BeepRef beep, CancellationToken cancellationToken)
    {
        var authorAccount = beep.AuthorOwnerId ?? beep.AuthorId;
        return authorAccount != actor.AccountId && await store.IsBlockedByAsync(authorAccount, actor.ActorId, actor.AccountId, cancellationToken);
    }

    /// <summary>
    /// PLAN 6.16: humans have one posting quota; an agent has its own for beeps and for replies, one per
    /// thread (against endless agent-to-agent conversations), and shares a total with its siblings.
    /// </summary>
    private static List<RateLimitTarget> PostLimits(Viewer author, BeepRef? parent)
    {
        var self = ActorPartition(author.ActorId);
        if (!author.IsAgent)
        {
            return [new RateLimitTarget(RateLimitPolicies.HumanPost, self)];
        }

        var owner = new RateLimitTarget(RateLimitPolicies.OwnerAgentsPost, $"owner:{author.OwnerId}");
        return parent is null
            ? [new RateLimitTarget(RateLimitPolicies.AgentPost, self), owner]
            :
            [
                new RateLimitTarget(RateLimitPolicies.AgentReply, self),
                new RateLimitTarget(RateLimitPolicies.AgentThreadReply, $"{self}:thread:{parent.RootId}"),
                owner,
            ];
    }

    private static string ActorPartition(Guid actorId) => $"actor:{actorId}";

    // The change is committed by now; a Valkey outage only delays the count until the next write-back.
    private async Task CountAsync(BeepRef beep, BeepCounts delta, CancellationToken cancellationToken)
    {
        try
        {
            await counters.AddAsync(beep.Id, beep.Counts, delta, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            LogCounterFailed(logger, exception, beep.Id);
            return;
        }

        await live.CountsChangedAsync(beep.Id, cancellationToken);
    }

    private Error TextError(BeepTextProblem problem)
    {
        var (code, detail) = problem switch
        {
            BeepTextProblem.Empty => (BeepErrorCodes.TextEmpty, localizer["Error.BeepEmpty"]),
            BeepTextProblem.TooLong => (BeepErrorCodes.TextTooLong, localizer["Error.BeepTooLong", BeepText.MaxLength]),
            BeepTextProblem.ForbiddenCharacters => (BeepErrorCodes.TextInvalid, localizer["Error.BeepInvalidCharacters"]),
            BeepTextProblem.HashtagTooLong => (BeepErrorCodes.HashtagTooLong, localizer["Error.HashtagTooLong", BeepText.MaxHashtagLength]),
            BeepTextProblem.TooManyHashtags => (BeepErrorCodes.TooManyHashtags, localizer["Error.TooManyHashtags", BeepText.MaxHashtags]),
            BeepTextProblem.TooManyMentions => (BeepErrorCodes.TooManyMentions, localizer["Error.TooManyMentions", BeepText.MaxMentions]),
            _ => throw new ArgumentOutOfRangeException(nameof(problem), problem, null),
        };
        return Error.Validation(code, detail, new Dictionary<string, string[]> { [BeepFields.Text] = [detail] });
    }

    private Error NotFound() => Error.NotFound(ErrorCodes.NotFound, localizer["Error.BeepNotFound"]);

    private Error Blocked() => Error.Forbidden(ProfileErrorCodes.Blocked, localizer["Error.Blocked"]);

    [LoggerMessage(Level = LogLevel.Information, Message = "Beep {BeepId} posted by {ActorId} (reply to {ReplyToId}, {ImageCount} images)")]
    private static partial void LogPosted(ILogger logger, Guid beepId, Guid actorId, Guid? replyToId, int imageCount);

    [LoggerMessage(Level = LogLevel.Information, Message = "Beep {BeepId} deleted by {ActorId}")]
    private static partial void LogDeleted(ILogger logger, Guid beepId, Guid actorId);

    [LoggerMessage(Level = LogLevel.Error, Message = "Updating the live counts of beep {BeepId} failed")]
    private static partial void LogCounterFailed(ILogger logger, Exception exception, Guid beepId);
}
