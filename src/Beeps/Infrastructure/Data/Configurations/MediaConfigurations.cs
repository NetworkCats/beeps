using Beeps.Features.Beeps;
using Beeps.Features.Media;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Beeps.Infrastructure.Data.Configurations;

internal sealed class MediaConfiguration : IEntityTypeConfiguration<MediaItem>
{
    public void Configure(EntityTypeBuilder<MediaItem> builder)
    {
        builder.ToTable("media");
        builder.HasKey(m => m.Id);
        builder.Property(m => m.Id).ValueGeneratedNever();
        builder.Property(m => m.StorageKey).HasMaxLength(MediaItem.StorageKeyLength);
        builder.HasIndex(m => m.StorageKey).IsUnique();
        builder.HasOne<Features.Accounts.Actor>().WithMany().HasForeignKey(m => m.OwnerActorId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(m => m.OwnerActorId);
        // The cleanup job: oldest uploads first.
        builder.HasIndex(m => m.CreatedAt);
    }
}

internal sealed class BeepMediaConfiguration : IEntityTypeConfiguration<BeepMedia>
{
    public void Configure(EntityTypeBuilder<BeepMedia> builder)
    {
        builder.ToTable("beep_media");
        // Also answers "which images does this beep have" for a page of beeps.
        builder.HasKey(bm => new { bm.BeepId, bm.MediaId });
        builder.HasOne<Beep>().WithMany().HasForeignKey(bm => bm.BeepId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<MediaItem>().WithMany().HasForeignKey(bm => bm.MediaId).OnDelete(DeleteBehavior.Restrict);
        // The cleanup job: is this upload used by any beep?
        builder.HasIndex(bm => bm.MediaId);
    }
}
