using Beeps.Features.Accounts;
using Beeps.Features.Admin;
using Beeps.Features.Agents;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Beeps.Infrastructure.Data.Configurations;

internal sealed class AgentKeyConfiguration : IEntityTypeConfiguration<AgentKey>
{
    public void Configure(EntityTypeBuilder<AgentKey> builder)
    {
        builder.ToTable("agent_keys");
        builder.HasKey(k => k.Id);
        builder.Property(k => k.Id).ValueGeneratedNever();
        builder.Property(k => k.KeyHash).HasMaxLength(64);
        builder.Property(k => k.KeyPrefix).HasMaxLength(32);
        builder.HasIndex(k => k.KeyHash).IsUnique();
        builder.HasIndex(k => k.AgentId);
        builder.HasOne<Actor>().WithMany().HasForeignKey(k => k.AgentId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class AdminAuditLogConfiguration : IEntityTypeConfiguration<AdminAuditLog>
{
    public void Configure(EntityTypeBuilder<AdminAuditLog> builder)
    {
        builder.ToTable("admin_audit_logs");
        builder.HasKey(l => l.Id);
        builder.Property(l => l.Id).ValueGeneratedNever();
        builder.Property(l => l.Action).HasMaxLength(AdminAuditLog.ActionMaxLength);
        builder.Property(l => l.TargetType).HasMaxLength(AdminAuditLog.TargetTypeMaxLength);
        builder.Property(l => l.Detail).HasMaxLength(AdminAuditLog.DetailMaxLength);
        builder.HasOne<Actor>().WithMany().HasForeignKey(l => l.AdminId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(l => l.AdminId);
    }
}
