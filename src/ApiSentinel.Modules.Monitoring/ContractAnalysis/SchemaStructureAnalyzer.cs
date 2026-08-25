using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ApiSentinel.Modules.Monitoring.Domain;
using Microsoft.Extensions.Options;

namespace ApiSentinel.Modules.Monitoring.ContractAnalysis;

public interface ISchemaStructureAnalyzer
{
    SchemaAnalysisResult Analyze(string? responseBody, IReadOnlyCollection<string> ignoredPaths);
}

public sealed class SchemaStructureAnalyzer(
    IOptions<ContractAnalysisOptions> options) : ISchemaStructureAnalyzer
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public SchemaAnalysisResult Analyze(
        string? responseBody,
        IReadOnlyCollection<string> ignoredPaths)
    {
        var fields = new List<SchemaField>();
        var status = SchemaAnalysisStatus.Complete;

        if (string.IsNullOrWhiteSpace(responseBody))
        {
            fields.Add(new SchemaField("$", "Empty"));
        }
        else
        {
            try
            {
                using var document = JsonDocument.Parse(responseBody);
                var visitor = new StructureVisitor(
                    fields,
                    new IgnoredPathMatcher(ignoredPaths),
                    options.Value.MaxDepth,
                    options.Value.MaxFields);
                visitor.Visit(document.RootElement, "$", depth: 0);
                status = visitor.Status;
            }
            catch (JsonException exception) when (
                exception.Message.Contains("depth", StringComparison.OrdinalIgnoreCase))
            {
                fields.Add(new SchemaField("$", "TooComplex"));
                status = SchemaAnalysisStatus.TooComplex;
            }
            catch (JsonException)
            {
                // Non-JSON successful responses still have a value-free structural contract.
                fields.Add(new SchemaField("$", "NonJson"));
            }
        }

        var canonicalFields = fields
            .OrderBy(field => field.Path, StringComparer.Ordinal)
            .ToArray();
        var structureJson = JsonSerializer.Serialize(canonicalFields, SerializerOptions);
        var structureHash = Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(structureJson)));
        return new SchemaAnalysisResult(structureJson, structureHash, status);
    }

    private sealed class StructureVisitor(
        List<SchemaField> fields,
        IgnoredPathMatcher ignoredPaths,
        int maxDepth,
        int maxFields)
    {
        private int _fieldCount;

        public SchemaAnalysisStatus Status { get; private set; } = SchemaAnalysisStatus.Complete;

        public void Visit(JsonElement element, string path, int depth)
        {
            if (Status == SchemaAnalysisStatus.TooComplex || ignoredPaths.Matches(path))
            {
                return;
            }

            if (depth > maxDepth || (path != "$" && _fieldCount >= maxFields))
            {
                Status = SchemaAnalysisStatus.TooComplex;
                return;
            }

            fields.Add(new SchemaField(path, TypeOf(element, depth)));
            if (path != "$")
            {
                _fieldCount++;
            }

            if (Status == SchemaAnalysisStatus.TooComplex)
            {
                return;
            }

            switch (element.ValueKind)
            {
                case JsonValueKind.Object:
                    VisitObjectProperties(element, path, depth + 1);
                    break;
                case JsonValueKind.Array when element.GetArrayLength() > 0:
                    VisitArrayElement(element[0], path, depth + 1);
                    break;
            }
        }

        private void VisitArrayElement(JsonElement element, string path, int depth)
        {
            if (depth > maxDepth)
            {
                Status = SchemaAnalysisStatus.TooComplex;
                return;
            }

            if (element.ValueKind == JsonValueKind.Object)
            {
                VisitObjectProperties(element, path, depth + 1);
            }
            else if (element.ValueKind == JsonValueKind.Array && element.GetArrayLength() > 0)
            {
                VisitArrayElement(element[0], path, depth + 1);
            }
        }

        private void VisitObjectProperties(JsonElement element, string parentPath, int childDepth)
        {
            foreach (var property in element
                         .EnumerateObject()
                         .GroupBy(property => property.Name, StringComparer.Ordinal)
                         .Select(group => group.Last())
                         .OrderBy(property => property.Name, StringComparer.Ordinal))
            {
                if (Status == SchemaAnalysisStatus.TooComplex)
                {
                    return;
                }

                var childPath = parentPath == "$"
                    ? property.Name
                    : $"{parentPath}.{property.Name}";
                Visit(property.Value, childPath, childDepth);
            }
        }

        private string TypeOf(JsonElement element, int depth) => element.ValueKind switch
        {
            JsonValueKind.Object => "Object",
            JsonValueKind.Array => ArrayTypeOf(element, depth),
            JsonValueKind.String => "String",
            JsonValueKind.Number => "Number",
            JsonValueKind.True or JsonValueKind.False => "Boolean",
            JsonValueKind.Null => "Null",
            _ => "Undefined"
        };

        private string ArrayTypeOf(JsonElement array, int depth)
        {
            var nesting = 0;
            var current = array;
            var result = new StringBuilder();

            while (current.ValueKind == JsonValueKind.Array)
            {
                nesting++;
                result.Append("Array<");
                if (depth + nesting > maxDepth)
                {
                    Status = SchemaAnalysisStatus.TooComplex;
                    result.Append("TooComplex");
                    break;
                }

                if (current.GetArrayLength() == 0)
                {
                    result.Append("Empty");
                    break;
                }

                current = current[0];
            }

            if (current.ValueKind != JsonValueKind.Array &&
                !result.ToString().EndsWith("TooComplex", StringComparison.Ordinal) &&
                !result.ToString().EndsWith("Empty", StringComparison.Ordinal))
            {
                result.Append(current.ValueKind switch
                {
                    JsonValueKind.Object => "Object",
                    JsonValueKind.String => "String",
                    JsonValueKind.Number => "Number",
                    JsonValueKind.True or JsonValueKind.False => "Boolean",
                    JsonValueKind.Null => "Null",
                    _ => "Undefined"
                });
            }

            result.Append('>', nesting);
            return result.ToString();
        }
    }
}
