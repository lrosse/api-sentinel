using ApiSentinel.Modules.Monitoring.Scheduling;
using Hangfire;
using Hangfire.Common;
using Hangfire.Storage;

namespace ApiSentinel.Infrastructure.Scheduling;

internal sealed class HangfireMonitorScheduleManager(
    IRecurringJobManager recurringJobs,
    JobStorage jobStorage) : IMonitorScheduleManager
{
    private const string JobIdPrefix = "monitor:";

    public Task UpsertAsync(
        MonitorSchedule schedule,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        recurringJobs.AddOrUpdate(
            GetJobId(schedule.MonitorId),
            Job.FromExpression<IScheduledMonitorJob>(job =>
                job.ExecuteAsync(schedule.MonitorId, CancellationToken.None)),
            ToCronExpression(schedule.IntervalSeconds),
            new RecurringJobOptions
            {
                TimeZone = TimeZoneInfo.Utc,
                MisfireHandling = MisfireHandlingMode.Relaxed
            });
        return Task.CompletedTask;
    }

    public Task RemoveAsync(
        Guid monitorId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        recurringJobs.RemoveIfExists(GetJobId(monitorId));
        return Task.CompletedTask;
    }

    public async Task ReconcileAsync(
        IReadOnlyCollection<MonitorSchedule> schedules,
        CancellationToken cancellationToken = default)
    {
        var expectedJobIds = schedules
            .Select(schedule => GetJobId(schedule.MonitorId))
            .ToHashSet(StringComparer.Ordinal);

        using var connection = jobStorage.GetConnection();
        var existingJobIds = connection
            .GetRecurringJobs()
            .Select(job => job.Id)
            .Where(jobId => jobId.StartsWith(JobIdPrefix, StringComparison.Ordinal))
            .ToList();

        foreach (var orphanJobId in existingJobIds.Where(jobId => !expectedJobIds.Contains(jobId)))
        {
            cancellationToken.ThrowIfCancellationRequested();
            recurringJobs.RemoveIfExists(orphanJobId);
        }

        foreach (var schedule in schedules)
        {
            await UpsertAsync(schedule, cancellationToken);
        }
    }

    private static string GetJobId(Guid monitorId) => $"{JobIdPrefix}{monitorId:N}";

    private static string ToCronExpression(int intervalSeconds)
    {
        var intervalMinutes = intervalSeconds / 60;
        if (intervalMinutes < 60)
        {
            return $"*/{intervalMinutes} * * * *";
        }

        var intervalHours = intervalMinutes / 60;
        if (intervalHours < 24)
        {
            return $"0 */{intervalHours} * * *";
        }

        return "0 0 * * *";
    }
}
