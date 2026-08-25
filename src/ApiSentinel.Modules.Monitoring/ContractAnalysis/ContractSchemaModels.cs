using System.Text.Json.Serialization;
using ApiSentinel.Modules.Monitoring.Domain;

namespace ApiSentinel.Modules.Monitoring.ContractAnalysis;

public sealed record SchemaField(
    [property: JsonPropertyName("path")] string Path,
    [property: JsonPropertyName("type")] string Type);

public sealed record ContractFieldChange(
    [property: JsonPropertyName("path")] string Path,
    [property: JsonPropertyName("changeType")] ContractChangeType ChangeType,
    [property: JsonPropertyName("oldType")] string? OldType,
    [property: JsonPropertyName("newType")] string? NewType);

public sealed record SchemaAnalysisResult(
    string StructureJson,
    string StructureHash,
    SchemaAnalysisStatus Status);

public sealed record ContractComparisonResult(
    ContractChangeClassification Classification,
    IReadOnlyCollection<ContractFieldChange> Changes,
    string ChangesJson);
