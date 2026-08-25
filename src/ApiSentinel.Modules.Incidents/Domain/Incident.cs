using ApiSentinel.Modules.Monitoring.Domain;
using MonitorEntity = ApiSentinel.Modules.Monitoring.Domain.Monitor;

namespace ApiSentinel.Modules.Incidents.Domain;

public sealed class Incident
{
    public Guid Id { get; set; }
    public Guid MonitorId { get; set; }
    public IncidentStatus Status { get; set; }
    public DateTime OpenedAt { get; set; }
    public DateTime? RecoveredAt { get; set; }
    public DateTime? ResolvedAt { get; set; }
    public required string TriggerReason { get; set; }
    public string? RootCause { get; set; }
    public required MonitorEntity Monitor { get; set; }
    public List<IncidentEvent> Events { get; set; } = [];
}

public sealed class IncidentEvent
{
    public Guid Id { get; set; }
    public Guid IncidentId { get; set; }
    public DateTime OccurredAt { get; set; }
    public IncidentEventType EventType { get; set; }
    public required string Description { get; set; }
    public Guid? RelatedCheckRunId { get; set; }
    public Guid? RelatedContractChangeId { get; set; }
    public required Incident Incident { get; set; }
    public CheckRun? RelatedCheckRun { get; set; }
    public ContractChange? RelatedContractChange { get; set; }
}

public enum IncidentStatus
{
    Open,
    Recovered,
    Resolved
}

public enum IncidentEventType
{
    Opened,
    EvidenceAdded,
    Recovered,
    ResolvedManually,
    CommentAdded
}
