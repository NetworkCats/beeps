using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Beeps.Infrastructure.Jobs;

/// <summary>When each scheduled job last ran, shared by all instances so a job runs once per interval in total.</summary>
public sealed class ScheduledJobState
{
    public const int NameMaxLength = 100;

    public required string Name { get; init; }

    public DateTimeOffset? LastStartedAt { get; init; }

    public DateTimeOffset? LastFinishedAt { get; init; }

    /// <summary>Why the last run failed; null after a successful one.</summary>
    public string? LastError { get; init; }
}

internal sealed class ScheduledJobStateConfiguration : IEntityTypeConfiguration<ScheduledJobState>
{
    public void Configure(EntityTypeBuilder<ScheduledJobState> builder)
    {
        builder.ToTable("scheduled_jobs");
        builder.HasKey(j => j.Name);
        builder.Property(j => j.Name).HasMaxLength(ScheduledJobState.NameMaxLength);
        builder.Property(j => j.LastError).HasMaxLength(FailedAttempts.ErrorMaxLength);
    }
}
