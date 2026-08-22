using ApiSentinel.Modules.Monitoring.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace ApiSentinel.Modules.Monitoring.HttpExecution;

internal enum MonitorRunState
{
    Completed,
    NotFound,
    Disabled,
    AlreadyRunning
}

internal sealed record MonitorRunResult(MonitorRunState State, CheckRun? CheckRun = null);

internal interface IMonitorRunner
{
    Task<MonitorRunResult> ExecuteAsync(
        Guid monitorId,
        string? ownerUserId,
        bool requireEnabled,
        CancellationToken cancellationToken);
}

internal sealed class MonitorRunner(
    IMonitoringDbContext dbContext,
    IHttpMonitorExecutor executor,
    IMonitorExecutionGate executionGate,
    ILogger<MonitorRunner> logger) : IMonitorRunner
{
    public async Task<MonitorRunResult> ExecuteAsync(
        Guid monitorId,
        string? ownerUserId,
        bool requireEnabled,
        CancellationToken cancellationToken)
    {
        var monitor = await dbContext.Monitors
            .Include(candidate => candidate.Endpoint)
            .ThenInclude(endpoint => endpoint.ApiService)
            .FirstOrDefaultAsync(
                candidate => candidate.Id == monitorId &&
                             (ownerUserId == null ||
                              candidate.Endpoint.ApiService.OwnerUserId == ownerUserId),
                cancellationToken);
        if (monitor is null)
        {
            return new MonitorRunResult(MonitorRunState.NotFound);
        }

        if (requireEnabled && !monitor.Enabled)
        {
            return new MonitorRunResult(MonitorRunState.Disabled);
        }

        using var lease = executionGate.TryEnter(monitorId);
        if (lease is null)
        {
            logger.LogInformation(
                "Check skipped for monitor {MonitorId} because another execution is in progress.",
                monitorId);
            return new MonitorRunResult(MonitorRunState.AlreadyRunning);
        }

        var checkRun = await executor.ExecuteAsync(monitor, cancellationToken);
        dbContext.CheckRuns.Add(checkRun);
        await dbContext.SaveChangesAsync(cancellationToken);
        return new MonitorRunResult(MonitorRunState.Completed, checkRun);
    }
}
