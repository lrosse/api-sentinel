namespace ApiSentinel.Modules.Monitoring.ContractAnalysis;

public sealed class ContractAnalysisOptions
{
    public const string SectionName = "Monitoring:ContractAnalysis";

    public int MaxDepth { get; set; } = 10;
    public int MaxFields { get; set; } = 500;
}
