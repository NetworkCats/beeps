using Beeps.Features.Moderation;
using Beeps.Infrastructure.Queues;
using Microsoft.Extensions.Options;

namespace Beeps.Infrastructure.Moderation;

/// <summary>
/// Runs one claimed review with retry: after the last attempt the beep is left visible, marked failed, for the admins.
/// </summary>
public sealed class ModerationProcessor(
    IModerationJobStore store,
    ModerationService moderation,
    IOptions<ModerationOptions> options,
    TimeProvider time,
    ILogger<ModerationProcessor> logger) : BeepJobProcessor(store, options.Value, time, logger)
{
    protected override string Work => "Review";

    protected override Task RunAsync(Guid beepId, CancellationToken cancellationToken) => moderation.ReviewAsync(beepId, cancellationToken);

    protected override Task GiveUpAsync(Guid beepId, string error, CancellationToken cancellationToken) => moderation.GiveUpAsync(beepId, error, cancellationToken);
}
