namespace Beeps.Infrastructure.Jobs;

public static class ScheduledJobSetup
{
    public static IServiceCollection AddBeepsScheduledJobs(this IServiceCollection services)
    {
        services.AddOptions<ScheduledJobOptions>().BindConfiguration(ScheduledJobOptions.SectionName);
        services.AddSingleton<IScheduledJobStore, PostgresScheduledJobStore>();
        services.AddScoped<ScheduledJobRunner>();
        services.AddHostedService<ScheduledJobWorker>();
        return services;
    }
}
