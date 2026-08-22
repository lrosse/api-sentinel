using ApiSentinel.Modules.Monitoring.HttpExecution;
using Microsoft.Extensions.Logging;

namespace ApiSentinel.Modules.Monitoring.Scheduling;

internal sealed class ScheduledMonitorJob(
    IMonitorRunner runner,
    ILogger<ScheduledMonitorJob> logger) : IScheduledMonitorJob
{
    public async Task ExecuteAsync(Guid monitorId, CancellationToken cancellationToken)
    {
        var result = await runner.ExecuteAsync(
            monitorId,
            ownerUserId: null,
            requireEnabled: true,
            cancellationToken);

        if (result.State is MonitorRunState.NotFound or MonitorRunState.Disabled)
        {
            logger.LogInformation(
                "Scheduled check skipped because monitor {MonitorId} no longer exists or is paused.",
                monitorId);
        }
    }
}
