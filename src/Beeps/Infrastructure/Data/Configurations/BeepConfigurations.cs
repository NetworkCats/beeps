using Beeps.Features.Accounts;
using Beeps.Features.Beeps;
using Beeps.Features.Notifications;
using Beeps.Features.Profiles;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Beeps.Infrastructure.Data.Configurations;

internal sealed class BeepConfiguration : IEntityTypeConfiguration<Beep>
{
    public void Configure(EntityTypeBuilder<Beep> builder)
    {
        builder.ToTable("beeps");
        builder.HasKey(b => b.Id);
        builder.Property(b => b.Id).ValueGeneratedNever();
        builder.Property(b => b.Text).HasMaxLength(BeepText.MaxStorageLength);
        builder.Property(b => b.ModerationStatus).HasConversion(EnumText.Converter<ModerationStatus>()).HasMaxLength(16);
        builder.HasOne(b => b.Author).WithMany().HasForeignKey(b => b.AuthorId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Beep>().WithMany().HasForeignKey(b => b.ReplyToId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Beep>().WithMany().HasForeignKey(b => b.RootId).OnDelete(DeleteBehavior.Restrict);
        // Profile and home timelines: newest beeps of each author, walked per author (LATERAL). It carries what
        // decides whether a beep is shown, so those walks never visit the table itself.
        builder.HasIndex(b => new { b.AuthorId, b.Id })
            .IsDescending(false, true)
            .IncludeProperties(b => new { b.ReplyToId, b.ModerationStatus, b.DeletedAt });
        // The public timeline: newest top-level beeps, without walking past deleted ones and replies.
        builder.HasIndex(b => b.Id).IsDescending(true).HasFilter("reply_to_id IS NULL AND deleted_at IS NULL").HasDatabaseName("ix_beeps_public");
        // The admins' review queue: hidden beeps and failed reviews, oldest first.
        builder.HasIndex(b => b.Id, "ix_beeps_review")
            .HasFilter("moderation_status IN ('hidden', 'failed') AND deleted_at IS NULL")
            .HasDatabaseName("ix_beeps_review");
        // Threads, oldest first.
        builder.HasIndex(b => new { b.RootId, b.Id });
        // Recounting replies (reply_to_id also gets its foreign-key index), off the index alone: which replies still count
        // is in it too. Both columns are in ix_beeps_author_id_id already, so this costs updates nothing more.
        builder.HasIndex(b => b.ReplyToId).IncludeProperties(b => new { b.DeletedAt, b.ModerationStatus });
    }
}

internal sealed class LikeConfiguration : IEntityTypeConfiguration<Like>
{
    public void Configure(EntityTypeBuilder<Like> builder)
    {
        builder.ToTable("likes");
        // Also answers "did the viewer like this beep" and recounts a beep's likes.
        builder.HasKey(l => new { l.BeepId, l.ActorId });
        builder.HasOne<Beep>().WithMany().HasForeignKey(l => l.BeepId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Actor>().WithMany().HasForeignKey(l => l.ActorId).OnDelete(DeleteBehavior.Restrict);
        // A profile's likes, most recently liked first.
        builder.HasIndex(l => new { l.ActorId, l.CreatedAt, l.BeepId });
    }
}

internal sealed class ViewedBeepConfiguration : IEntityTypeConfiguration<ViewedBeep>
{
    public void Configure(EntityTypeBuilder<ViewedBeep> builder)
    {
        builder.ToTable("viewed_beeps");
        // One row per person and beep: a view again is counted on it.
        builder.HasKey(v => new { v.UserId, v.BeepId });
        builder.HasOne<User>().WithMany().HasForeignKey(v => v.UserId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Beep>().WithMany().HasForeignKey(v => v.BeepId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(v => v.BeepId);
    }
}

internal sealed class MentionConfiguration : IEntityTypeConfiguration<Mention>
{
    public void Configure(EntityTypeBuilder<Mention> builder)
    {
        builder.ToTable("mentions");
        builder.HasKey(m => new { m.BeepId, m.ActorId });
        builder.HasOne<Beep>().WithMany().HasForeignKey(m => m.BeepId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Actor>().WithMany().HasForeignKey(m => m.ActorId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(m => m.ActorId);
    }
}

internal sealed class HashtagConfiguration : IEntityTypeConfiguration<Hashtag>
{
    public const string UniqueName = "ux_hashtags_name";

    public void Configure(EntityTypeBuilder<Hashtag> builder)
    {
        builder.ToTable("hashtags");
        builder.HasKey(h => h.Id);
        builder.Property(h => h.Id).ValueGeneratedNever();
        builder.Property(h => h.Name).HasMaxLength(BeepText.MaxHashtagStorageLength);
        builder.HasIndex(h => h.Name).IsUnique().HasDatabaseName(UniqueName);
    }
}

internal sealed class BeepHashtagConfiguration : IEntityTypeConfiguration<BeepHashtag>
{
    public void Configure(EntityTypeBuilder<BeepHashtag> builder)
    {
        builder.ToTable("beep_hashtags");
        builder.HasKey(t => new { t.BeepId, t.HashtagId });
        builder.HasOne<Beep>().WithMany().HasForeignKey(t => t.BeepId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Hashtag>().WithMany().HasForeignKey(t => t.HashtagId).OnDelete(DeleteBehavior.Restrict);
        // Hashtag pages (PLAN 8): newest beeps with the tag.
        builder.HasIndex(t => new { t.HashtagId, t.BeepId }).IsDescending(false, true);
    }
}

internal sealed class FollowConfiguration : IEntityTypeConfiguration<Follow>
{
    public void Configure(EntityTypeBuilder<Follow> builder)
    {
        builder.ToTable("follows", table => table.HasCheckConstraint("ck_follows_not_self", "follower_id <> followee_id"));
        builder.HasKey(f => new { f.FollowerId, f.FolloweeId });
        // To users, not actors: both sides must be humans (PLAN 8).
        builder.HasOne<User>().WithMany().HasForeignKey(f => f.FollowerId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<User>().WithMany().HasForeignKey(f => f.FolloweeId).OnDelete(DeleteBehavior.Restrict);
        // A human's lists of whom they follow and who follows them, most recent first.
        builder.HasIndex(f => new { f.FollowerId, f.CreatedAt, f.FolloweeId });
        builder.HasIndex(f => new { f.FolloweeId, f.CreatedAt, f.FollowerId });
    }
}

internal sealed class NotificationConfiguration : IEntityTypeConfiguration<Notification>
{
    public void Configure(EntityTypeBuilder<Notification> builder)
    {
        builder.ToTable("notifications");
        builder.HasKey(n => n.Id);
        builder.Property(n => n.Id).ValueGeneratedNever();
        builder.Property(n => n.Type).HasConversion(EnumText.Converter<NotificationType>()).HasMaxLength(16);
        builder.HasOne<Actor>().WithMany().HasForeignKey(n => n.RecipientId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Actor>().WithMany().HasForeignKey(n => n.FromActorId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Beep>().WithMany().HasForeignKey(n => n.BeepId).OnDelete(DeleteBehavior.Restrict);
        // The inbox, newest first: one head per type and beep, so a beep's thousands of likes are one row here. The
        // unique index makes generating notifications idempotent.
        builder.HasIndex(n => new { n.RecipientId, n.Id }, "ix_notifications_heads")
            .IsDescending(false, true)
            .HasFilter("NOT superseded")
            .HasDatabaseName("ix_notifications_heads");
        builder.HasIndex(n => new { n.RecipientId, n.Id }, "ix_notifications_unread_heads")
            .IsDescending(false, true)
            .HasFilter("NOT superseded AND read_at IS NULL")
            .HasDatabaseName("ix_notifications_unread_heads");
        builder.HasIndex(n => new { n.RecipientId, n.Type, n.FromActorId, n.BeepId }, "ux_notifications_once").IsUnique().HasDatabaseName("ux_notifications_once");
        builder.HasIndex(n => new { n.RecipientId, n.Id }, "ix_notifications_unread")
            .IsDescending(false, true)
            .HasFilter("read_at IS NULL")
            .HasDatabaseName("ix_notifications_unread");
        // The notifications about a beep: those an inbox entry stands for (likes of it), and those read once it is seen.
        builder.HasIndex(n => new { n.RecipientId, n.BeepId, n.Type, n.Id }, "ix_notifications_beep")
            .IsDescending(false, false, false, true)
            .HasDatabaseName("ix_notifications_beep");
        // Who has unread ones about a beep or from an actor (InboxRecounter), off the index alone: a beep's likes or a
        // spammer's notifications can number in the hundreds of thousands, almost all read.
        builder.HasIndex(n => n.FromActorId).IncludeProperties(n => new { n.RecipientId, n.ReadAt });
        builder.HasIndex(n => n.BeepId).IncludeProperties(n => new { n.RecipientId, n.ReadAt });
    }
}
