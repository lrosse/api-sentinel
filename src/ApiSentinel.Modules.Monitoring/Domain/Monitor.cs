using ApiSentinel.Modules.ApiCatalog.Domain;

namespace ApiSentinel.Modules.Monitoring.Domain;

public sealed class Monitor
{
    public Guid Id { get; set; }
    public Guid EndpointId { get; set; }
    public int TimeoutMs { get; set; }
    public int ExpectedStatusCode { get; set; }
    public int? MaxLatencyMs { get; set; }
    public int IntervalSeconds { get; set; }
    public bool Enabled { get; set; }
    public List<string> IgnoredPaths { get; set; } = [];
    public required Endpoint Endpoint { get; set; }
    public List<CheckRun> CheckRuns { get; set; } = [];
    public List<SchemaSnapshot> SchemaSnapshots { get; set; } = [];
    public List<ContractChange> ContractChanges { get; set; } = [];
}
