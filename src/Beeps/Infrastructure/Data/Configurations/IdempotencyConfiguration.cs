using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Beeps.Infrastructure.Data.Configurations;

internal sealed class IdempotencyConfiguration : IEntityTypeConfiguration<IdempotencyEntry>
{
    public void Configure(EntityTypeBuilder<IdempotencyEntry> builder)
    {
        builder.ToTable("idempotency_keys");
        builder.HasKey(e => e.Key);
        builder.Property(e => e.Key).HasMaxLength(IdempotencyEntry.KeyMaxLength);
        builder.Property(e => e.Fingerprint).HasMaxLength(64);
        builder.Property(e => e.ContentType).HasMaxLength(200);
        builder.Property(e => e.Location).HasMaxLength(2000);
        // The cleanup job.
        builder.HasIndex(e => e.ExpiresAt);
    }
}
