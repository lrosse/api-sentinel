namespace ApiSentinel.Modules.Monitoring.Domain;

public sealed class ContractChange
{
    public Guid Id { get; set; }
    public Guid MonitorId { get; set; }
    public DateTime DetectedAt { get; set; }
    public Guid FromSnapshotId { get; set; }
    public Guid ToSnapshotId { get; set; }
    public ContractChangeClassification Classification { get; set; }
    public required string ChangesJson { get; set; }
    public required Monitor Monitor { get; set; }
    public required SchemaSnapshot FromSnapshot { get; set; }
    public required SchemaSnapshot ToSnapshot { get; set; }
}

public enum ContractChangeClassification
{
    Compatible,
    Breaking
}

public enum ContractChangeType
{
    Added,
    Removed,
    TypeChanged
}
