using Beeps.Infrastructure.Data;
using Npgsql;

namespace Beeps.Tests.Infrastructure.Data;

public sealed class DatabaseSetupTests
{
    [Fact]
    public void StatementsArePreparedFromTheFirstUseAndPlannedForEachRun()
    {
        var settings = new NpgsqlConnectionStringBuilder("Host=db;Options=-c search_path=public");

        DatabaseSetup.Prepare(settings);

        Assert.Equal(DatabaseSetup.MaxAutoPrepare, settings.MaxAutoPrepare);
        Assert.Equal(1, settings.AutoPrepareMinUsages);
        Assert.Equal("-c search_path=public -c plan_cache_mode=force_custom_plan", settings.Options);
    }

    [Theory]
    [InlineData("Host=db;Max Auto Prepare=0")]
    [InlineData("Host=db;MaxAutoPrepare=0")]
    [InlineData("Host=db;max auto prepare=0")]
    public void TheConnectionStringMayTurnItOff(string connectionString)
    {
        var settings = new NpgsqlConnectionStringBuilder(connectionString);

        DatabaseSetup.Prepare(settings);

        Assert.Equal(0, settings.MaxAutoPrepare);
        Assert.Null(settings.Options);
    }

    [Fact]
    public void TheConnectionStringKeepsItsOwnPlanCacheMode()
    {
        var settings = new NpgsqlConnectionStringBuilder("Host=db;Auto Prepare Min Usages=3;Options=-c plan_cache_mode=auto");

        DatabaseSetup.Prepare(settings);

        Assert.Equal(3, settings.AutoPrepareMinUsages);
        Assert.Equal("-c plan_cache_mode=auto", settings.Options);
    }
}
