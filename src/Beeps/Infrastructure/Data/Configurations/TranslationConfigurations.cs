using Beeps.Features.Beeps;
using Beeps.Features.Translation;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Beeps.Infrastructure.Data.Configurations;

internal sealed class TranslationJobConfiguration : IEntityTypeConfiguration<TranslationJob>
{
    public void Configure(EntityTypeBuilder<TranslationJob> builder)
    {
        builder.ToTable("translation_jobs");
        builder.HasKey(j => j.Id);
        builder.Property(j => j.Id).ValueGeneratedNever();
        builder.Property(j => j.LastError).HasMaxLength(FailedAttempts.ErrorMaxLength);
        builder.HasOne<Beep>().WithMany().HasForeignKey(j => j.BeepId).OnDelete(DeleteBehavior.Cascade);
        // One translation per beep.
        builder.HasIndex(j => j.BeepId).IsUnique();
        builder.HasIndex(j => j.RunAfter);
    }
}

internal sealed class BeepTranslationConfiguration : IEntityTypeConfiguration<BeepTranslation>
{
    public void Configure(EntityTypeBuilder<BeepTranslation> builder)
    {
        builder.ToTable("beep_translations");
        builder.HasKey(t => t.BeepId);
        builder.Property(t => t.BeepId).ValueGeneratedNever();
        builder.Property(t => t.Language).HasMaxLength(BeepTranslation.LanguageMaxLength);
        builder.Property(t => t.English).HasMaxLength(BeepText.MaxStorageLength);
        builder.Property(t => t.Chinese).HasMaxLength(BeepText.MaxStorageLength);
        builder.Property(t => t.Model).HasMaxLength(BeepTranslation.ModelMaxLength);
        builder.Property(t => t.Provider).HasMaxLength(BeepTranslation.ProviderMaxLength);
        builder.HasOne<Beep>().WithOne().HasForeignKey<BeepTranslation>(t => t.BeepId).OnDelete(DeleteBehavior.Cascade);
    }
}
