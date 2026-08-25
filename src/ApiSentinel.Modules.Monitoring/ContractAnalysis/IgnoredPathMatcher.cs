namespace ApiSentinel.Modules.Monitoring.ContractAnalysis;

internal sealed class IgnoredPathMatcher(IEnumerable<string> ignoredPaths)
{
    private readonly string[] _paths = ignoredPaths
        .Where(path => !string.IsNullOrWhiteSpace(path))
        .Select(Normalize)
        .Where(path => path.Length > 0)
        .Distinct(StringComparer.Ordinal)
        .ToArray();

    public bool Matches(string path)
    {
        var normalized = Normalize(path);
        return _paths.Any(ignored =>
            normalized.Equals(ignored, StringComparison.Ordinal) ||
            normalized.StartsWith($"{ignored}.", StringComparison.Ordinal));
    }

    private static string Normalize(string path)
    {
        var normalized = path.Trim();
        return normalized.StartsWith("$.", StringComparison.Ordinal)
            ? normalized[2..]
            : normalized;
    }
}
