using Beeps.Features.Live;
using Beeps.Features.Media;
using Beeps.Features.Moderation;
using Beeps.Features.Notifications;
using Beeps.Features.Profiles;
using Beeps.Features.RateLimiting;
using Beeps.Features.Search;
using Beeps.Features.Shared;
using Beeps.Features.Translation;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Options;

namespace Beeps.Features.Beeps;

/// <summary>Posting, replying, deleting and liking, for humans and agents alike; and what humans view.</summary>
public sealed partial class BeepService(
    IBeepStore store,
    IBeepCounterStore counters,
    IMediaStore mediaStore,
    MediaService media,
    BeepViews views,
    IUnitOfWork unitOfWork,
    IOutbox outbox,
    IModerationJobs moderationJobs,
    ITranslationJobs translationJobs,
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

    private static readonly LogThrottle ViewFailures = new();
    private static readonly LogThrottle CounterFailures = new();

    /// <summary>
    /// Posts a beep, or a reply when <paramref name="replyToId"/> is set, with up to four images the author
    /// uploaded (<paramref name="mediaIds"/>; with images the text may be empty). Mentions resolve to actors
    /// that exist right now; hashtags, mentions, images, the notification job and the review (or, with no review, the
    /// translation) commit together with the beep. Nobody replies to someone who blocked them.
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

        var allowed = await MayPostAsync(author, replyToId, cancellationToken);
        return allowed.IsSuccess ? await PostAllowedAsync(author, parsed, allowed.Value, images, cancellationToken) : allowed.Error;
    }

    /// <summary>
    /// Whether the author may post now (and reply to <paramref name="replyToId"/>): the parent is there for them and
    /// its author did not block them, and they are within the posting limits, which this counts against. Its value is the parent.
    /// </summary>
    private async Task<Result<BeepRef?>> MayPostAsync(Viewer author, Guid? replyToId, CancellationToken cancellationToken)
    {
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
        return limited.IsSuccess ? Result.Of(parent) : limited.Error;
    }

    private async Task<Result<BeepView>> PostAllowedAsync(Viewer author, ParsedBeepText parsed, BeepRef? parent, IReadOnlyList<Guid> images, CancellationToken cancellationToken)
    {
        IReadOnlyList<ActorRow> mentioned = parsed.MentionHandles.Count == 0
            ? []
            : await store.FindActorsByHandlesAsync(parsed.MentionHandles, cancellationToken);
        var now = time.GetUtcNow();
        var id = Guid.CreateVersion7(now);
        // Only text is reviewed, so a beep of images alone has nothing to wait for; with moderation
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
                if (images.Count > 0 && (await mediaStore.LockOwnedAsync(author.ActorId, images, MediaPurpose.Beep, ct)).Count != images.Count)
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
                else if (parsed.Text.Length > 0)
                {
                    // Let through as it is: translated now, rather than once a review lets it through.
                    translationJobs.Enqueue(id);
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
    /// A beep written on the web, with its image files: they are uploaded, then posted with the text. Everything
    /// else is checked first (the text, the number of images, the parent, blocks, the posting limits), so a beep that
    /// cannot be posted uploads nothing.
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

        var allowed = await MayPostAsync(author, replyToId, cancellationToken);
        if (!allowed.IsSuccess)
        {
            return allowed.Error;
        }

        var uploaded = await media.UploadAllAsync(author, files, cancellationToken);
        return uploaded.IsSuccess ? await PostAllowedAsync(author, parsed, allowed.Value, [.. uploaded.Value.Distinct()], cancellationToken) : uploaded.Error;
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

        return await ListThreadAsync(viewer, beep.RootId, after, limit, cancellationToken);
    }

    /// <summary>The thread of a beep just shown to the viewer (<see cref="GetAsync"/>), which needs no second look for it.</summary>
    public async Task<CursorPage<BeepView>> GetThreadOfAsync(Viewer? viewer, BeepView shown, Guid? after, int? limit, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(shown);
        return await ListThreadAsync(viewer, shown.RootId, after, limit, cancellationToken);
    }

    /// <summary>Authors delete their own beeps; a human also deletes their agents' beeps.</summary>
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

        if (await RemoveAsync(beep, viewer.ActorId, cancellationToken))
        {
            LogDeleted(logger, beepId, viewer.ActorId);
        }

        return Result.Success;
    }

    /// <summary>
    /// Beeps of images alone whose last image could not be decoded: posted while it was processing, they
    /// would stay up with nothing in them.
    /// </summary>
    public async Task DeleteEmptyAsync(IReadOnlyCollection<Guid> beepIds, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(beepIds);
        foreach (var beepId in beepIds.Count == 0 ? [] : await store.ListEmptyAsync(beepIds, cancellationToken))
        {
            if (await store.FindVisibleAsync(beepId, null, cancellationToken) is { } beep && await RemoveAsync(beep, null, cancellationToken))
            {
                LogDeletedEmpty(logger, beepId);
            }
        }
    }

    /// <summary>Deletes the beep and takes it off pages, inboxes and its parent's count; false when it was gone already.</summary>
    private async Task<bool> RemoveAsync(BeepRef beep, Guid? actorId, CancellationToken cancellationToken)
    {
        var beepId = beep.Id;
        ModerationStatus? deleted = null;
        await unitOfWork.InTransactionAsync(
            null,
            async ct =>
            {
                deleted = await store.DeleteAsync(beepId, time.GetUtcNow(), ct);
                if (deleted is not null)
                {
                    outbox.SyncBeepToSearch(beepId);
                    await unitOfWork.SaveChangesAsync(ct);
                }

                return Result.Success;
            },
            cancellationToken);
        if (deleted is null)
        {
            return false;
        }

        // Deleted already: what follows is done even if the request is given up on now, or the parent's count would
        // stay one too high until something else changed it.
        await live.BeepRemovedAsync(beepId, CancellationToken.None);
        await inboxes.RecountAsync([beepId], [], CancellationToken.None);
        // Its author still sees a hidden reply, but its parent's count already left it out.
        if (beep.ReplyToId is { } parentId && deleted != ModerationStatus.Hidden)
        {
            await ReplyCountChangedAsync(parentId, actorId, -1, CancellationToken.None);
        }

        return true;
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

        var limited = await rateLimits.CheckAsync(RateLimitPolicies.Like, RateLimitPartitions.Actor(viewer.ActorId), cancellationToken);
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
        if (await store.RecordViewAsync(viewer.ActorId, beepId, now, now - RepeatViewWindow, cancellationToken))
        {
            await CountViewAsync(beepId, cancellationToken);
        }

        return Result.Success;
    }

    // Written back with the like and reply counts; straight to the column when Valkey cannot hold it.
    private async Task CountViewAsync(Guid beepId, CancellationToken cancellationToken)
    {
        try
        {
            if (await counters.AddViewAsync(beepId, cancellationToken))
            {
                return;
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            if (ViewFailures.Allow())
            {
                LogViewFailed(logger, exception, beepId);
            }
        }

        await store.AddViewsAsync(new Dictionary<Guid, long> { [beepId] = 1 }, cancellationToken);
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
        var limited = await rateLimits.CheckAsync(RateLimitPolicies.Like, RateLimitPartitions.Actor(viewer.ActorId), cancellationToken);
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
        await ReplyCountChangedAsync(parentId, null, visible ? 1 : -1, cancellationToken);
    }

    // A parent out of sight (hidden, its author banned or blocking the one deleting) cannot be counted in place, yet may
    // be shown again later: it is recounted from its rows instead, or its count would stay wrong for good.
    private async Task ReplyCountChangedAsync(Guid parentId, Guid? viewerId, int delta, CancellationToken cancellationToken)
    {
        if (await store.FindVisibleAsync(parentId, viewerId, cancellationToken) is { } parent)
        {
            await CountAsync(parent, new BeepCounts(0, delta), cancellationToken);
            return;
        }

        outbox.Enqueue(BeepRecountRequest.MessageType, new BeepRecountRequest(parentId), BeepCountsJsonContext.Default.BeepRecountRequest);
        await unitOfWork.SaveChangesAsync(CancellationToken.None);
    }

    /// <summary>Whether the human behind the beep's author blocked the actor, or the actor's owner.</summary>
    private async Task<bool> BlockedByAuthorAsync(Viewer actor, BeepRef beep, CancellationToken cancellationToken)
    {
        var authorAccount = beep.AuthorOwnerId ?? beep.AuthorId;
        return authorAccount != actor.AccountId && await store.IsBlockedByAsync(authorAccount, actor.ActorId, actor.AccountId, cancellationToken);
    }

    /// <summary>
    /// Humans have one posting quota; an agent has its own for beeps and for replies, one per
    /// thread (against endless agent-to-agent conversations), and shares a total with its siblings.
    /// </summary>
    private static List<RateLimitTarget> PostLimits(Viewer author, BeepRef? parent)
    {
        var self = RateLimitPartitions.Actor(author.ActorId);
        if (!author.IsAgent)
        {
            return [new RateLimitTarget(RateLimitPolicies.HumanPost, self)];
        }

        var owner = new RateLimitTarget(RateLimitPolicies.OwnerAgentsPost, RateLimitPartitions.Owner(author.AccountId));
        return parent is null
            ? [new RateLimitTarget(RateLimitPolicies.AgentPost, self), owner]
            :
            [
                new RateLimitTarget(RateLimitPolicies.AgentReply, self),
                new RateLimitTarget(RateLimitPolicies.AgentThreadReply, RateLimitPartitions.Thread(author.ActorId, parent.RootId)),
                owner,
            ];
    }

    // The change is committed by now. Without Valkey the beep is not marked for write-back either, so a recount is
    // stored instead: it puts the column right now and the live count once Valkey is back.
    private async Task CountAsync(BeepRef beep, BeepCounts delta, CancellationToken cancellationToken)
    {
        try
        {
            await counters.AddAsync(beep.Id, beep.Counts, delta, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            if (CounterFailures.Allow())
            {
                LogCounterFailed(logger, exception, beep.Id);
            }
            outbox.Enqueue(BeepRecountRequest.MessageType, new BeepRecountRequest(beep.Id), BeepCountsJsonContext.Default.BeepRecountRequest);
            await unitOfWork.SaveChangesAsync(CancellationToken.None);
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
        return Error.Field(code, BeepFields.Text, detail);
    }

    private Error NotFound() => Error.NotFound(ErrorCodes.NotFound, localizer["Error.BeepNotFound"]);

    private Error Blocked() => Error.Forbidden(ProfileErrorCodes.Blocked, localizer["Error.Blocked"]);

    [LoggerMessage(Level = LogLevel.Information, Message = "Beep {BeepId} posted by {ActorId} (reply to {ReplyToId}, {ImageCount} images)")]
    private static partial void LogPosted(ILogger logger, Guid beepId, Guid actorId, Guid? replyToId, int imageCount);

    [LoggerMessage(Level = LogLevel.Information, Message = "Beep {BeepId} deleted by {ActorId}")]
    private static partial void LogDeleted(ILogger logger, Guid beepId, Guid actorId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Beep {BeepId} deleted: its images could not be decoded and it has no text")]
    private static partial void LogDeletedEmpty(ILogger logger, Guid beepId);

    [LoggerMessage(Level = LogLevel.Error, Message = "Updating the live counts of beep {BeepId} failed (logged at most once a minute)")]
    private static partial void LogCounterFailed(ILogger logger, Exception exception, Guid beepId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Noting a view of beep {BeepId} in Valkey failed; it is counted in its column at once (logged at most once a minute)")]
    private static partial void LogViewFailed(ILogger logger, Exception exception, Guid beepId);
}
