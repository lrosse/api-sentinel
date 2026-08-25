namespace ApiSentinel.Modules.Monitoring.Domain;

public sealed class SchemaSnapshot
{
    public Guid Id { get; set; }
    public Guid MonitorId { get; set; }
    public DateTime CapturedAt { get; set; }
    public required string StructureHash { get; set; }
    public required string StructureJson { get; set; }
    public SchemaAnalysisStatus AnalysisStatus { get; set; }
    public required Monitor Monitor { get; set; }
}

public enum SchemaAnalysisStatus
{
    Complete,
    TooComplex
}
