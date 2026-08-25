using ApiSentinel.Modules.Monitoring.ContractAnalysis;
using ApiSentinel.Modules.Monitoring.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MonitorEntity = ApiSentinel.Modules.Monitoring.Domain.Monitor;

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
    ISchemaStructureAnalyzer schemaAnalyzer,
    IContractSchemaComparer schemaComparer,
    IMonitorRunIncidentEvaluator incidentEvaluator,
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

        var execution = await executor.ExecuteAsync(monitor, cancellationToken);
        var checkRun = execution.CheckRun;
        dbContext.CheckRuns.Add(checkRun);

        ContractChange? contractChange = null;
        if (checkRun.Status == CheckRunStatus.Success)
        {
            contractChange = await CaptureContractAsync(
                monitor,
                checkRun,
                execution.ResponseBody,
                cancellationToken);
        }

        await incidentEvaluator.EvaluateAsync(
            monitor,
            checkRun,
            contractChange,
            cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
        return new MonitorRunResult(MonitorRunState.Completed, checkRun);
    }

    private async Task<ContractChange?> CaptureContractAsync(
        MonitorEntity monitor,
        CheckRun checkRun,
        string? responseBody,
        CancellationToken cancellationToken)
    {
        var previousSnapshot = await dbContext.SchemaSnapshots
            .Where(snapshot => snapshot.MonitorId == monitor.Id)
            .OrderByDescending(snapshot => snapshot.CapturedAt)
            .ThenByDescending(snapshot => snapshot.Id)
            .FirstOrDefaultAsync(cancellationToken);
        var analysis = schemaAnalyzer.Analyze(responseBody, monitor.IgnoredPaths);
        var snapshot = new SchemaSnapshot
        {
            Id = Guid.NewGuid(),
            MonitorId = monitor.Id,
            CapturedAt = checkRun.FinishedAt,
            StructureHash = analysis.StructureHash,
            StructureJson = analysis.StructureJson,
            AnalysisStatus = analysis.Status,
            Monitor = monitor
        };
        dbContext.SchemaSnapshots.Add(snapshot);

        if (previousSnapshot is null ||
            previousSnapshot.AnalysisStatus != SchemaAnalysisStatus.Complete ||
            snapshot.AnalysisStatus != SchemaAnalysisStatus.Complete ||
            previousSnapshot.StructureHash.Equals(snapshot.StructureHash, StringComparison.Ordinal))
        {
            return null;
        }

        var comparison = schemaComparer.Compare(
            previousSnapshot.StructureJson,
            snapshot.StructureJson,
            monitor.IgnoredPaths);
        if (comparison.Changes.Count == 0)
        {
            return null;
        }

        var contractChange = new ContractChange
        {
            Id = Guid.NewGuid(),
            MonitorId = monitor.Id,
            DetectedAt = checkRun.FinishedAt,
            FromSnapshotId = previousSnapshot.Id,
            ToSnapshotId = snapshot.Id,
            Classification = comparison.Classification,
            ChangesJson = comparison.ChangesJson,
            Monitor = monitor,
            FromSnapshot = previousSnapshot,
            ToSnapshot = snapshot
        };
        dbContext.ContractChanges.Add(contractChange);
        return contractChange;
    }
}
