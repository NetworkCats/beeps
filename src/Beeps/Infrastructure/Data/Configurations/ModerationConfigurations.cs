using Beeps.Features.Accounts;
using Beeps.Features.Beeps;
using Beeps.Features.Moderation;
using Beeps.Features.Profiles;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Beeps.Infrastructure.Data.Configurations;

internal sealed class ModerationJobConfiguration : IEntityTypeConfiguration<ModerationJob>
{
    public void Configure(EntityTypeBuilder<ModerationJob> builder)
    {
        builder.ToTable("moderation_jobs");
        builder.HasKey(j => j.Id);
        builder.Property(j => j.Id).ValueGeneratedNever();
        builder.Property(j => j.LastError).HasMaxLength(FailedAttempts.ErrorMaxLength);
        builder.HasOne<Beep>().WithMany().HasForeignKey(j => j.BeepId).OnDelete(DeleteBehavior.Cascade);
        // One review per beep; also how an admin's decision finds the pending one to drop.
        builder.HasIndex(j => j.BeepId).IsUnique();
        builder.HasIndex(j => j.RunAfter);
    }
}

internal sealed class ModerationResultConfiguration : IEntityTypeConfiguration<ModerationResult>
{
    public void Configure(EntityTypeBuilder<ModerationResult> builder)
    {
        builder.ToTable("moderation_results");
        builder.HasKey(r => r.Id);
        builder.Property(r => r.Id).ValueGeneratedNever();
        builder.Property(r => r.Model).HasMaxLength(ModerationResult.ModelMaxLength);
        builder.Property(r => r.Provider).HasMaxLength(ModerationResult.ProviderMaxLength);
        builder.Property(r => r.Decision).HasConversion(EnumText.Converter<ModerationDecision>()).HasMaxLength(16);
        builder.Property(r => r.Severity).HasConversion(EnumText.Converter<ModerationSeverity>()).HasMaxLength(16);
        builder.Property(r => r.Reason).HasMaxLength(ModerationResult.ReasonMaxLength);
        builder.HasOne<Beep>().WithMany().HasForeignKey(r => r.BeepId).OnDelete(DeleteBehavior.Restrict);
        // A beep's results, newest first, on the review pages.
        builder.HasIndex(r => new { r.BeepId, r.Id }).IsDescending(false, true);
    }
}

internal sealed class ReportConfiguration : IEntityTypeConfiguration<Report>
{
    private const string UniqueOpen = "ux_reports_open";

    public void Configure(EntityTypeBuilder<Report> builder)
    {
        builder.ToTable("reports");
        builder.HasKey(r => r.Id);
        builder.Property(r => r.Id).ValueGeneratedNever();
        builder.Property(r => r.Reason).HasMaxLength(32);
        builder.Property(r => r.Note).HasMaxLength(Report.NoteMaxLength * 4);
        builder.HasOne<Beep>().WithMany().HasForeignKey(r => r.BeepId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Actor>().WithMany().HasForeignKey(r => r.ReporterId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Actor>().WithMany().HasForeignKey(r => r.ResolvedBy).OnDelete(DeleteBehavior.Restrict);
        // One open report per person and beep; as its leading column, beep_id also finds a beep's open reports.
        builder.HasIndex(r => new { r.BeepId, r.ReporterId }).IsUnique().HasFilter("resolved_at IS NULL").HasDatabaseName(UniqueOpen);
        // Every report of a beep, open or not, for its review page.
        builder.HasIndex(r => new { r.BeepId, r.Id }).IsDescending(false, true);
        builder.HasIndex(r => r.ReporterId);
        builder.HasIndex(r => r.ResolvedBy);
    }
}

internal sealed class BlockConfiguration : IEntityTypeConfiguration<Block>
{
    public void Configure(EntityTypeBuilder<Block> builder)
    {
        builder.ToTable("blocks", table => table.HasCheckConstraint("ck_blocks_not_self", "blocker_id <> blocked_id"));
        // Also answers "whom did this human block", which every signed-in read asks.
        builder.HasKey(k => new { k.BlockerId, k.BlockedId });
        builder.HasOne<User>().WithMany().HasForeignKey(k => k.BlockerId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Actor>().WithMany().HasForeignKey(k => k.BlockedId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(k => k.BlockedId);
    }
}
