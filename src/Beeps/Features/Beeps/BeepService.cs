using Beeps.Features.Moderation;
using Beeps.Features.Notifications;
using Beeps.Features.RateLimiting;
using Beeps.Features.Shared;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Options;

namespace Beeps.Features.Beeps;

/// <summary>Posting, replying, deleting and liking (PLAN 2.2, 6.6, 6.19), for humans and agents alike.</summary>
public sealed partial class BeepService(
    IBeepStore store,
    IBeepCounterStore counters,
    BeepViews views,
    IUnitOfWork unitOfWork,
    IOutbox outbox,
    RateLimitService rateLimits,
    IOptions<ModerationOptions> moderation,
    TimeProvider time,
    IStringLocalizer<SharedResource> localizer,
    ILogger<BeepService> logger)
{
    /// <summary>
    /// Posts a beep, or a reply when <paramref name="replyToId"/> is set. Mentions resolve to actors that
    /// exist right now; hashtags, mentions and the notification job commit together with the beep.
    /// </summary>
    public async Task<Result<BeepView>> PostAsync(Viewer author, string? text, Guid? replyToId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(author);
        var parsed = BeepText.Parse(text);
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
        var beep = new Beep
        {
            Id = id,
            AuthorId = author.ActorId,
            Text = parsed.Text,
            ReplyToId = parent?.Id,
            RootId = parent?.RootId ?? id,
            // With moderation off (local development) nothing will ever review it, so it is not left pending.
            ModerationStatus = moderation.Value.Enabled ? ModerationStatus.Pending : ModerationStatus.Allowed,
            CreatedAt = now,
        };
        await unitOfWork.InTransactionAsync(
            null,
            async ct =>
            {
                IReadOnlyDictionary<string, Guid> hashtags = parsed.Hashtags.Count == 0
                    ? new Dictionary<string, Guid>()
                    : await store.EnsureHashtagsAsync(parsed.Hashtags, now, ct);
                store.AddBeep(beep, [.. mentioned.Select(m => m.Id)], [.. hashtags.Values]);
                if (parent is not null || mentioned.Count > 0)
                {
                    outbox.Enqueue(BeepNotificationsRequest.MessageType, new BeepNotificationsRequest(id), NotificationsJsonContext.Default.BeepNotificationsRequest);
                }

                await unitOfWork.SaveChangesAsync(ct);
                return Result.Success;
            },
            cancellationToken);

        if (parent is not null)
        {
            await CountAsync(parent, new BeepCounts(0, 1), cancellationToken);
        }

        LogPosted(logger, id, author.ActorId, parent?.Id);
        return await GetAsync(author, id, cancellationToken);
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

        if (!await store.DeleteAsync(beepId, time.GetUtcNow(), cancellationToken))
        {
            return Result.Success;
        }

        LogDeleted(logger, beepId, viewer.ActorId);
        if (beep.ReplyToId is { } parentId && await store.FindVisibleAsync(parentId, viewer.ActorId, cancellationToken) is { } parent)
        {
            await CountAsync(parent, new BeepCounts(0, -1), cancellationToken);
        }

        return Result.Success;
    }

    /// <summary>Liking twice is the same as liking once; only the first like notifies the author.</summary>
    public async Task<Result> LikeAsync(Viewer viewer, Guid beepId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(viewer);
        var beep = await store.FindVisibleAsync(beepId, viewer.ActorId, cancellationToken);
        if (beep is null)
        {
            return NotFound();
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
        }

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
        }

        return Result.Success;
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
        }
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

    [LoggerMessage(Level = LogLevel.Information, Message = "Beep {BeepId} posted by {ActorId} (reply to {ReplyToId})")]
    private static partial void LogPosted(ILogger logger, Guid beepId, Guid actorId, Guid? replyToId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Beep {BeepId} deleted by {ActorId}")]
    private static partial void LogDeleted(ILogger logger, Guid beepId, Guid actorId);

    [LoggerMessage(Level = LogLevel.Error, Message = "Updating the live counts of beep {BeepId} failed")]
    private static partial void LogCounterFailed(ILogger logger, Exception exception, Guid beepId);
}
