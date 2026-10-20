using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Beeps.Infrastructure.Outbox;
using Microsoft.EntityFrameworkCore;

namespace Beeps.Infrastructure.Data;

public sealed class BeepsDbContext(DbContextOptions<BeepsDbContext> options) : DbContext(options), IDataProtectionKeyContext
{
    public DbSet<DataProtectionKey> DataProtectionKeys => Set<DataProtectionKey>();

    /// <summary>Workers to wake once the save in progress, which commits itself, is done (<see cref="WorkSignalInterceptor"/>).</summary>
    internal WorkQueues WorkAfterSave { get; set; }

    /// <summary>Workers to wake once this context's transaction commits (<see cref="WorkSignalInterceptor"/>).</summary>
    internal WorkQueues WorkAfterCommit { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(BeepsDbContext).Assembly);
    }
}
