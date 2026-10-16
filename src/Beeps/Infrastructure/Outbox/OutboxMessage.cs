using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Beeps.Infrastructure.Outbox;

/// <summary>A pending message. <see cref="RunAfter"/> doubles as the claim lease while a worker holds it.</summary>
public sealed class OutboxMessage
{
    public const int TypeMaxLength = 100;

    public Guid Id { get; init; }

    public required string Type { get; init; }

    public required string Payload { get; init; }

    public int Attempts { get; init; }

    public DateTimeOffset RunAfter { get; init; }

    public DateTimeOffset CreatedAt { get; init; }

    public string? LastError { get; init; }
}

/// <summary>A message that ran out of attempts; kept for inspection, retry or discard from the admin area.</summary>
public sealed class OutboxDeadMessage
{
    public Guid Id { get; init; }

    public required string Type { get; init; }

    public required string Payload { get; init; }

    public int Attempts { get; init; }

    public required string LastError { get; init; }

    public DateTimeOffset CreatedAt { get; init; }

    public DateTimeOffset FailedAt { get; init; }
}

internal sealed class OutboxMessageConfiguration : IEntityTypeConfiguration<OutboxMessage>
{
    public void Configure(EntityTypeBuilder<OutboxMessage> builder)
    {
        builder.ToTable("outbox");
        builder.HasKey(m => m.Id);
        builder.Property(m => m.Id).ValueGeneratedNever();
        builder.Property(m => m.Type).HasMaxLength(OutboxMessage.TypeMaxLength);
        builder.Property(m => m.Payload).HasColumnType("jsonb");
        builder.HasIndex(m => m.RunAfter);
    }
}

internal sealed class OutboxDeadMessageConfiguration : IEntityTypeConfiguration<OutboxDeadMessage>
{
    public void Configure(EntityTypeBuilder<OutboxDeadMessage> builder)
    {
        builder.ToTable("outbox_dead");
        builder.HasKey(m => m.Id);
        builder.Property(m => m.Id).ValueGeneratedNever();
        builder.Property(m => m.Type).HasMaxLength(OutboxMessage.TypeMaxLength);
        builder.Property(m => m.Payload).HasColumnType("jsonb");
        builder.HasIndex(m => m.FailedAt);
    }
}
