using Beeps.Features.Media;
using Beeps.Features.Shared;
using Microsoft.Extensions.Options;

namespace Beeps.Features.Avatars;

/// <summary>An account to look up on Gravatar.</summary>
/// <param name="NeedsAvatar">False for one that has an avatar already, is banned or deleted: it is only marked as looked up.</param>
public sealed record GravatarCheck(Guid UserId, string Email, bool NeedsAvatar);

/// <summary>
/// Gives each account its Gravatar image, if its address has one, as a first avatar: once its address is verified, and
/// once only. The image becomes an ordinary custom avatar (<see cref="AvatarService.ImportAsync"/>), that the person
/// can replace or remove like any other. Accounts made before this existed are looked up too, a batch at a time.
/// </summary>
public sealed partial class GravatarImportJob(
    IAvatarStore store,
    IGravatar gravatar,
    AvatarService avatars,
    IOptions<GravatarOptions> options,
    TimeProvider time,
    ILogger<GravatarImportJob> logger) : IScheduledJob
{
    public const string JobName = "avatars.gravatar";

    /// <summary>Looked up one at a time, so a run asks Gravatar this many times at most.</summary>
    public const int BatchSize = 50;

    public string Name => JobName;

    /// <summary>Often, so a new account shows its Gravatar image soon after its address is verified.</summary>
    public TimeSpan Interval => TimeSpan.FromMinutes(1);

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        if (!options.Value.Enabled)
        {
            return;
        }

        var imported = 0;
        var checks = await store.ListGravatarChecksAsync(BatchSize, cancellationToken);
        foreach (var check in checks)
        {
            // Gravatar being away throws before the account is marked: it is asked again next run.
            if (check.NeedsAvatar && await gravatar.FindAsync(check.Email, cancellationToken) is { } image)
            {
                using var stream = new MemoryStream(image, writable: false);
                var result = await avatars.ImportAsync(check.UserId, new MediaContent(stream, image.Length), cancellationToken);
                if (result.IsSuccess)
                {
                    imported++;
                }
                else
                {
                    // An image the uploads refuse (too small, not an image) never gets better.
                    LogRefused(logger, check.UserId, result.Error.Code);
                }
            }

            await store.MarkGravatarCheckedAsync(check.UserId, time.GetUtcNow(), cancellationToken);
        }

        if (checks.Count > 0)
        {
            LogChecked(logger, checks.Count, imported);
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Looked up {Count} accounts on Gravatar and imported {Imported} avatars")]
    private static partial void LogChecked(ILogger logger, int count, int imported);

    [LoggerMessage(Level = LogLevel.Information, Message = "The Gravatar image of {UserId} was not taken: {Code}")]
    private static partial void LogRefused(ILogger logger, Guid userId, string code);
}
