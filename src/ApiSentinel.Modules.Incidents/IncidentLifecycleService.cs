using ApiSentinel.Modules.Incidents.Domain;
using ApiSentinel.Modules.Monitoring;
using ApiSentinel.Modules.Monitoring.ContractAnalysis;
using ApiSentinel.Modules.Monitoring.Domain;
using Microsoft.EntityFrameworkCore;
using MonitorEntity = ApiSentinel.Modules.Monitoring.Domain.Monitor;

namespace ApiSentinel.Modules.Incidents;

internal sealed class IncidentLifecycleService(IIncidentsDbContext dbContext) :
    IMonitorRunIncidentEvaluator,
    IActiveIncidentReader
{
    public async Task EvaluateAsync(
        MonitorEntity monitor,
        CheckRun checkRun,
        ContractChange? contractChange,
        CancellationToken cancellationToken)
    {
        var openIncident = await dbContext.Incidents
            .Where(incident => incident.MonitorId == monitor.Id &&
                               incident.Status == IncidentStatus.Open)
            .OrderByDescending(incident => incident.OpenedAt)
            .ThenByDescending(incident => incident.Id)
            .FirstOrDefaultAsync(cancellationToken);

        if (contractChange?.Classification == ContractChangeClassification.Breaking)
        {
            HandleBreakingChange(monitor, checkRun, contractChange, openIncident);
            return;
        }

        if (checkRun.Status == CheckRunStatus.Success)
        {
            if (openIncident is not null)
            {
                openIncident.Status = IncidentStatus.Recovered;
                openIncident.RecoveredAt = checkRun.FinishedAt;
                dbContext.IncidentEvents.Add(CreateEvent(
                    openIncident,
                    checkRun.FinishedAt,
                    IncidentEventType.Recovered,
                    "Execução bem-sucedida detectada; incidente marcado como recuperado.",
                    checkRun.Id,
                    null));
            }

            return;
        }

        if (openIncident is not null)
        {
            dbContext.IncidentEvents.Add(CreateEvent(
                openIncident,
                checkRun.FinishedAt,
                IncidentEventType.EvidenceAdded,
                DescribeFailureEvidence(checkRun),
                checkRun.Id,
                null));
            return;
        }

        var previousStatuses = await dbContext.CheckRuns
            .AsNoTracking()
            .Where(run => run.MonitorId == monitor.Id)
            .OrderByDescending(run => run.StartedAt)
            .ThenByDescending(run => run.Id)
            .Select(run => run.Status)
            .Take(monitor.ConsecutiveFailuresThreshold - 1)
            .ToListAsync(cancellationToken);
        var consecutiveFailures = 1 + previousStatuses.TakeWhile(
            status => status == CheckRunStatus.Failure).Count();

        if (consecutiveFailures < monitor.ConsecutiveFailuresThreshold)
        {
            return;
        }

        var reason = $"{consecutiveFailures} falhas consecutivas.";
        CreateIncident(
            monitor,
            checkRun.FinishedAt,
            reason,
            "Incidente aberto após o monitor atingir o limite de falhas consecutivas.",
            checkRun.Id,
            null);
    }

    public async Task<IReadOnlyDictionary<Guid, ActiveIncidentSummary>> GetActiveByMonitorAsync(
        IReadOnlyCollection<Guid> monitorIds,
        CancellationToken cancellationToken)
    {
        if (monitorIds.Count == 0)
        {
            return new Dictionary<Guid, ActiveIncidentSummary>();
        }

        var active = await dbContext.Incidents
            .AsNoTracking()
            .Where(incident => monitorIds.Contains(incident.MonitorId) &&
                               incident.Status != IncidentStatus.Resolved)
            .OrderBy(incident => incident.Status == IncidentStatus.Open ? 0 : 1)
            .ThenByDescending(incident => incident.OpenedAt)
            .ThenByDescending(incident => incident.Id)
            .Select(incident => new
            {
                incident.MonitorId,
                incident.Id,
                incident.Status
            })
            .ToListAsync(cancellationToken);

        return active
            .GroupBy(incident => incident.MonitorId)
            .ToDictionary(
                group => group.Key,
                group =>
                {
                    var incident = group.First();
                    return new ActiveIncidentSummary(incident.Id, incident.Status.ToString());
                });
    }

    private void HandleBreakingChange(
        MonitorEntity monitor,
        CheckRun checkRun,
        ContractChange contractChange,
        Incident? openIncident)
    {
        var breakingPath = ContractSchemaComparer.DeserializeChanges(contractChange.ChangesJson)
            .FirstOrDefault(change => change.ChangeType is
                ContractChangeType.Removed or ContractChangeType.TypeChanged)
            ?.Path ?? "path não identificado";
        var reason = $"Mudança de contrato quebradora detectada em {breakingPath}.";

        if (openIncident is not null)
        {
            dbContext.IncidentEvents.Add(CreateEvent(
                openIncident,
                checkRun.FinishedAt,
                IncidentEventType.EvidenceAdded,
                reason,
                checkRun.Id,
                contractChange.Id));
            return;
        }

        CreateIncident(
            monitor,
            checkRun.FinishedAt,
            reason,
            reason,
            checkRun.Id,
            contractChange.Id);
    }

    private void CreateIncident(
        MonitorEntity monitor,
        DateTime occurredAt,
        string triggerReason,
        string eventDescription,
        Guid? relatedCheckRunId,
        Guid? relatedContractChangeId)
    {
        var incident = new Incident
        {
            Id = Guid.NewGuid(),
            MonitorId = monitor.Id,
            Status = IncidentStatus.Open,
            OpenedAt = occurredAt,
            TriggerReason = triggerReason,
            Monitor = monitor
        };
        incident.Events.Add(CreateEvent(
            incident,
            occurredAt,
            IncidentEventType.Opened,
            eventDescription,
            relatedCheckRunId,
            relatedContractChangeId));
        dbContext.Incidents.Add(incident);
    }

    private static IncidentEvent CreateEvent(
        Incident incident,
        DateTime occurredAt,
        IncidentEventType eventType,
        string description,
        Guid? relatedCheckRunId,
        Guid? relatedContractChangeId) =>
        new()
        {
            Id = Guid.NewGuid(),
            IncidentId = incident.Id,
            OccurredAt = occurredAt,
            EventType = eventType,
            Description = description,
            RelatedCheckRunId = relatedCheckRunId,
            RelatedContractChangeId = relatedContractChangeId,
            Incident = incident
        };

    private static string DescribeFailureEvidence(CheckRun checkRun)
    {
        var detail = !string.IsNullOrWhiteSpace(checkRun.ErrorMessage)
            ? checkRun.ErrorMessage
            : checkRun.HttpStatusCode is not null
                ? $"HTTP {checkRun.HttpStatusCode}"
                : "sem resposta HTTP";
        return $"Nova falha registrada: {detail}";
    }
}
