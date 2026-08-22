namespace ApiSentinel.Modules.Monitoring.HttpExecution;

public sealed class MonitoringHttpOptions
{
    public const string SectionName = "Monitoring:HttpExecution";

    public int MaxResponseBytes { get; set; } = 1_048_576;
    public int ResponseBodySnippetMaxChars { get; set; } = 4_096;
    public int MaxRedirects { get; set; } = 3;
}

public sealed class NetworkSecurityOptions
{
    public const string SectionName = "Monitoring:NetworkSecurity";

    /// <summary>
    /// Exact host names that may resolve to non-public addresses for the local Docker demo.
    /// Keep this list empty in production; this is not a wildcard or suffix allowlist.
    /// </summary>
    public List<string> DevelopmentInternalHosts { get; set; } = [];
}
