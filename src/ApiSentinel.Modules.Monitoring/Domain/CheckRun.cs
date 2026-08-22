namespace ApiSentinel.Modules.Monitoring.Domain;

public sealed class CheckRun
{
    public Guid Id { get; set; }
    public Guid MonitorId { get; set; }
    public DateTime StartedAt { get; set; }
    public DateTime FinishedAt { get; set; }
    public CheckRunStatus Status { get; set; }
    public int? HttpStatusCode { get; set; }
    public long LatencyMs { get; set; }
    public string? ErrorMessage { get; set; }
    public string? ResponseBodySnippet { get; set; }
    public required Monitor Monitor { get; set; }
}

public enum CheckRunStatus
{
    Success,
    Failure
}
