using Beeps.Features.Accounts;
using Beeps.Features.Site;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Beeps.Infrastructure.Data.Configurations;

internal sealed class ActorConfiguration : IEntityTypeConfiguration<Actor>
{
    public void Configure(EntityTypeBuilder<Actor> builder)
    {
        builder.ToTable("actors", table =>
        {
            table.HasCheckConstraint("ck_actors_human_named", "kind <> 'human' OR (handle IS NOT NULL AND display_name IS NOT NULL)");
            table.HasCheckConstraint("ck_actors_agent_owned", "kind <> 'agent' OR owner_id IS NOT NULL");
        });
        builder.HasKey(a => a.Id);
        builder.Property(a => a.Id).ValueGeneratedNever();
        builder.Property(a => a.Kind).HasConversion(EnumText.Converter<ActorKind>()).HasMaxLength(16);
        builder.Property(a => a.Handle).HasMaxLength(Actor.HandleMaxLength);
        builder.Property(a => a.NormalizedHandle).HasMaxLength(Actor.HandleMaxLength);
        builder.Property(a => a.DisplayName).HasMaxLength(Actor.DisplayNameMaxLength);
        builder.Property(a => a.Bio).HasMaxLength(Actor.BioMaxLength);
        builder.Property(a => a.Website).HasMaxLength(Actor.WebsiteMaxLength);
        builder.Property(a => a.ModelName).HasMaxLength(Actor.ModelNameMaxLength);
        // text_pattern_ops: the same index serves equality and the admin's prefix search (LIKE 'abc%').
        builder.HasIndex(a => a.NormalizedHandle).IsUnique().HasDatabaseName(AccountIndexes.UniqueHandle).HasOperators("text_pattern_ops");
        builder.HasIndex(a => a.OwnerId);
        builder.HasOne<Actor>().WithMany().HasForeignKey(a => a.OwnerId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(a => a.AvatarMedia).WithMany().HasForeignKey(a => a.AvatarMediaId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(a => a.BannerMedia).WithMany().HasForeignKey(a => a.BannerMediaId).OnDelete(DeleteBehavior.Restrict);
        // The cleanup job: is this upload anyone's avatar or banner?
        builder.HasIndex(a => a.AvatarMediaId);
        builder.HasIndex(a => a.BannerMediaId);
    }
}

internal sealed class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> builder)
    {
        builder.ToTable("users");
        builder.HasKey(u => u.Id);
        builder.Property(u => u.Id).ValueGeneratedNever();
        builder.HasOne(u => u.Actor).WithOne().HasForeignKey<User>(u => u.Id).OnDelete(DeleteBehavior.Restrict);
        builder.Property(u => u.Email).HasMaxLength(User.EmailMaxLength);
        builder.Property(u => u.NormalizedEmail).HasMaxLength(User.EmailMaxLength);
        builder.Property(u => u.PendingEmail).HasMaxLength(User.EmailMaxLength);
        builder.Property(u => u.Locale).HasMaxLength(16);
        builder.Property(u => u.Theme).HasMaxLength(Themes.MaxLength);
        builder.Property(u => u.UntranslatedLanguages).HasDefaultValueSql("'{}'");
        builder.HasIndex(u => u.NormalizedEmail).IsUnique().HasDatabaseName(AccountIndexes.UniqueEmail).HasOperators("text_pattern_ops");
        // Counting admins (last-admin rule, admin checks) only ever looks at this handful of rows.
        builder.HasIndex(u => u.Id).HasFilter("is_admin").HasDatabaseName("ix_users_admins");
        // The cleanup job: sign-ups that never verified, oldest first. Those it released already leave the index, or every
        // run would walk all of them again before getting to new ones.
        builder.HasIndex(u => u.CreatedAt, "ix_users_unverified")
            .HasFilter("email_verified_at IS NULL AND normalized_email NOT LIKE 'released:%'")
            .HasDatabaseName("ix_users_unverified");
        // The Gravatar import: verified accounts not looked up yet, oldest first. Each leaves the index once looked up.
        builder.HasIndex(u => u.CreatedAt, "ix_users_gravatar_unchecked")
            .HasFilter("gravatar_checked_at IS NULL AND email_verified_at IS NOT NULL")
            .HasDatabaseName("ix_users_gravatar_unchecked");
        // Visibility checks every author against the banned ones, who are few.
        builder.HasIndex(u => u.Id, "ix_users_banned").HasFilter("banned_at IS NOT NULL").HasDatabaseName("ix_users_banned");
    }
}

internal sealed class UserSessionConfiguration : IEntityTypeConfiguration<UserSession>
{
    public void Configure(EntityTypeBuilder<UserSession> builder)
    {
        builder.ToTable("user_sessions");
        builder.HasKey(s => s.Id);
        builder.Property(s => s.Id).ValueGeneratedNever();
        builder.Property(s => s.TokenHash).HasMaxLength(64);
        builder.Property(s => s.UserAgent).HasMaxLength(UserSession.UserAgentMaxLength);
        builder.Property(s => s.IpAddress).HasMaxLength(UserSession.IpAddressMaxLength);
        builder.HasIndex(s => s.TokenHash).IsUnique();
        // Revoking or forgetting all of a user's sessions.
        builder.HasIndex(s => s.UserId);
        // The cleanup job: ended by expiry, or by revocation (only revoked rows are indexed).
        builder.HasIndex(s => s.ExpiresAt);
        builder.HasIndex(s => s.RevokedAt).HasFilter("revoked_at IS NOT NULL");
        builder.HasOne<User>().WithMany().HasForeignKey(s => s.UserId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class ExternalLoginConfiguration : IEntityTypeConfiguration<ExternalLogin>
{
    public void Configure(EntityTypeBuilder<ExternalLogin> builder)
    {
        builder.ToTable("user_logins");
        // Signing in looks a login up by the provider's id for it.
        builder.HasKey(l => new { l.Provider, l.ProviderKey }).HasName(AccountIndexes.UniqueLogin);
        builder.Property(l => l.Provider).HasMaxLength(ExternalLogin.ProviderMaxLength);
        builder.Property(l => l.ProviderKey).HasMaxLength(ExternalLogin.KeyMaxLength);
        builder.Property(l => l.Name).HasMaxLength(ExternalLogin.NameMaxLength);
        // One of each provider per account; also the settings' list of them.
        builder.HasIndex(l => new { l.UserId, l.Provider }).IsUnique().HasDatabaseName(AccountIndexes.UniqueLoginProvider);
        builder.HasOne<User>().WithMany().HasForeignKey(l => l.UserId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class PasskeyConfiguration : IEntityTypeConfiguration<Passkey>
{
    public void Configure(EntityTypeBuilder<Passkey> builder)
    {
        builder.ToTable("user_passkeys");
        builder.HasKey(p => p.Id);
        builder.Property(p => p.Id).ValueGeneratedNever();
        builder.Property(p => p.CredentialId).HasMaxLength(Passkey.CredentialIdMaxLength);
        builder.Property(p => p.UserAgent).HasMaxLength(Passkey.UserAgentMaxLength);
        // Signing in looks a passkey up by the id the browser names it by; no two accounts can have the same one.
        builder.HasIndex(p => p.CredentialId).IsUnique().HasDatabaseName(AccountIndexes.UniquePasskey);
        // The settings' list of them.
        builder.HasIndex(p => p.UserId);
        builder.HasOne<User>().WithMany().HasForeignKey(p => p.UserId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class EmailCodeConfiguration : IEntityTypeConfiguration<EmailCode>
{
    public void Configure(EntityTypeBuilder<EmailCode> builder)
    {
        builder.ToTable("email_codes");
        builder.HasKey(c => new { c.Purpose, c.NormalizedEmail });
        builder.Property(c => c.Purpose).HasConversion(EnumText.Converter<EmailCodePurpose>()).HasMaxLength(32);
        builder.Property(c => c.NormalizedEmail).HasMaxLength(User.EmailMaxLength);
        builder.Property(c => c.CodeHash).HasMaxLength(64);
        // The cleanup job.
        builder.HasIndex(c => c.ExpiresAt);
    }
}

internal sealed class InviteConfiguration : IEntityTypeConfiguration<Invite>
{
    public void Configure(EntityTypeBuilder<Invite> builder)
    {
        builder.ToTable("invites");
        builder.HasKey(i => i.Id);
        builder.Property(i => i.Id).ValueGeneratedNever();
        builder.Property(i => i.CodeHash).HasMaxLength(64);
        builder.Property(i => i.CodePrefix).HasMaxLength(16);
        builder.HasIndex(i => i.CodeHash).IsUnique();
        builder.HasOne<Actor>().WithMany().HasForeignKey(i => i.CreatedBy).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Actor>().WithMany().HasForeignKey(i => i.UsedBy).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(i => i.CreatedBy);
        builder.HasIndex(i => i.UsedBy);
    }
}

internal sealed class SiteSettingConfiguration : IEntityTypeConfiguration<SiteSetting>
{
    public void Configure(EntityTypeBuilder<SiteSetting> builder)
    {
        builder.ToTable("site_settings");
        builder.HasKey(s => s.Key);
        builder.Property(s => s.Key).HasMaxLength(64);
        builder.Property(s => s.Value).HasMaxLength(1000);
    }
}
