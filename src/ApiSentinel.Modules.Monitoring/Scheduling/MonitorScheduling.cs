using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.EntityFrameworkCore;

namespace ApiSentinel.Modules.Monitoring.Scheduling;

public sealed record MonitorSchedule(Guid MonitorId, int IntervalSeconds);

public interface IMonitorScheduleManager
{
    Task UpsertAsync(MonitorSchedule schedule, CancellationToken cancellationToken = default);
    Task RemoveAsync(Guid monitorId, CancellationToken cancellationToken = default);
    Task ReconcileAsync(
        IReadOnlyCollection<MonitorSchedule> schedules,
        CancellationToken cancellationToken = default);
}

public interface IScheduledMonitorJob
{
    Task ExecuteAsync(Guid monitorId, CancellationToken cancellationToken);
}

internal sealed class DisabledMonitorScheduleManager : IMonitorScheduleManager
{
    public Task UpsertAsync(
        MonitorSchedule schedule,
        CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task RemoveAsync(
        Guid monitorId,
        CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task ReconcileAsync(
        IReadOnlyCollection<MonitorSchedule> schedules,
        CancellationToken cancellationToken = default) => Task.CompletedTask;
}

public static class MonitorScheduleReconciler
{
    public static async Task ReconcileMonitorSchedulesAsync(
        this IHost host,
        CancellationToken cancellationToken = default)
    {
        using var scope = host.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<IMonitoringDbContext>();
        var scheduleManager = scope.ServiceProvider.GetRequiredService<IMonitorScheduleManager>();
        var schedules = await dbContext.Monitors
            .AsNoTracking()
            .Where(monitor => monitor.Enabled)
            .OrderBy(monitor => monitor.Id)
            .Select(monitor => new MonitorSchedule(monitor.Id, monitor.IntervalSeconds))
            .ToListAsync(cancellationToken);

        await scheduleManager.ReconcileAsync(schedules, cancellationToken);
    }
}
