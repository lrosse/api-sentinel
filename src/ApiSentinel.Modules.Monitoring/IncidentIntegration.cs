using ApiSentinel.Modules.Monitoring.Domain;

namespace ApiSentinel.Modules.Monitoring;

public interface IMonitorRunIncidentEvaluator
{
    Task EvaluateAsync(
        Domain.Monitor monitor,
        CheckRun checkRun,
        ContractChange? contractChange,
        CancellationToken cancellationToken);
}

public interface IActiveIncidentReader
{
    Task<IReadOnlyDictionary<Guid, ActiveIncidentSummary>> GetActiveByMonitorAsync(
        IReadOnlyCollection<Guid> monitorIds,
        CancellationToken cancellationToken);
}

public sealed record ActiveIncidentSummary(Guid Id, string Status);

internal sealed class DisabledMonitorRunIncidentEvaluator : IMonitorRunIncidentEvaluator
{
    public Task EvaluateAsync(
        Domain.Monitor monitor,
        CheckRun checkRun,
        ContractChange? contractChange,
        CancellationToken cancellationToken) => Task.CompletedTask;
}

internal sealed class EmptyActiveIncidentReader : IActiveIncidentReader
{
    public Task<IReadOnlyDictionary<Guid, ActiveIncidentSummary>> GetActiveByMonitorAsync(
        IReadOnlyCollection<Guid> monitorIds,
        CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyDictionary<Guid, ActiveIncidentSummary>>(
            new Dictionary<Guid, ActiveIncidentSummary>());
}
