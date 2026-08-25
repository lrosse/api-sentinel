using System.Text.Json;
using System.Text.Json.Serialization;
using ApiSentinel.Modules.Monitoring.Domain;

namespace ApiSentinel.Modules.Monitoring.ContractAnalysis;

public interface IContractSchemaComparer
{
    ContractComparisonResult Compare(
        string previousStructureJson,
        string currentStructureJson,
        IReadOnlyCollection<string> ignoredPaths);
}

public sealed class ContractSchemaComparer : IContractSchemaComparer
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter(namingPolicy: null, allowIntegerValues: false) }
    };

    public static IReadOnlyCollection<ContractFieldChange> DeserializeChanges(string changesJson) =>
        JsonSerializer.Deserialize<ContractFieldChange[]>(changesJson, SerializerOptions) ?? [];

    public ContractComparisonResult Compare(
        string previousStructureJson,
        string currentStructureJson,
        IReadOnlyCollection<string> ignoredPaths)
    {
        var matcher = new IgnoredPathMatcher(ignoredPaths);
        var previous = Deserialize(previousStructureJson, matcher);
        var current = Deserialize(currentStructureJson, matcher);
        var changes = new List<ContractFieldChange>();

        foreach (var (path, oldType) in previous.OrderBy(item => item.Key, StringComparer.Ordinal))
        {
            if (!current.TryGetValue(path, out var newType))
            {
                changes.Add(new ContractFieldChange(
                    path,
                    ContractChangeType.Removed,
                    oldType,
                    null));
            }
            else if (!oldType.Equals(newType, StringComparison.Ordinal))
            {
                changes.Add(new ContractFieldChange(
                    path,
                    ContractChangeType.TypeChanged,
                    oldType,
                    newType));
            }
        }

        foreach (var (path, newType) in current.OrderBy(item => item.Key, StringComparer.Ordinal))
        {
            if (!previous.ContainsKey(path))
            {
                changes.Add(new ContractFieldChange(
                    path,
                    ContractChangeType.Added,
                    null,
                    newType));
            }
        }

        var orderedChanges = changes
            .OrderBy(change => change.Path, StringComparer.Ordinal)
            .ThenBy(change => change.ChangeType)
            .ToArray();
        var classification = orderedChanges.Any(change =>
            change.ChangeType is ContractChangeType.Removed or ContractChangeType.TypeChanged)
            ? ContractChangeClassification.Breaking
            : ContractChangeClassification.Compatible;

        return new ContractComparisonResult(
            classification,
            orderedChanges,
            JsonSerializer.Serialize(orderedChanges, SerializerOptions));
    }

    private static Dictionary<string, string> Deserialize(
        string structureJson,
        IgnoredPathMatcher matcher) =>
        (JsonSerializer.Deserialize<SchemaField[]>(structureJson, SerializerOptions) ?? [])
        .Where(field => !matcher.Matches(field.Path))
        .ToDictionary(field => field.Path, field => field.Type, StringComparer.Ordinal);
}
