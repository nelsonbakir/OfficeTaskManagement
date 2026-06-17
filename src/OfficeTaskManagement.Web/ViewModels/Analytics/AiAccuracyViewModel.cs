namespace OfficeTaskManagement.ViewModels.Analytics;

/// <summary>T53: View model for the AI Accuracy analytics dashboard.</summary>
public class AiAccuracyViewModel
{
    public List<AiAccuracyTypeRow>  ByType     { get; set; } = new();
    public List<AiAccuracyMonthRow> ByMonth    { get; set; } = new();
    public List<AiTokenUsageRow>    TokenUsage { get; set; } = new();
    public int TotalLogs    { get; set; }
    public int LogsWithData { get; set; }
}

public class AiAccuracyTypeRow
{
    public string EntityType     { get; set; } = string.Empty;
    public int    Count          { get; set; }
    public double AvgAiHours     { get; set; }
    public double AvgActualHours { get; set; }
    /// <summary>Positive = AI under-estimated (actual > AI). Negative = AI over-estimated.</summary>
    public double AvgDeltaPct    { get; set; }
}

public class AiAccuracyMonthRow
{
    public string Month          { get; set; } = string.Empty;
    public int    Count          { get; set; }
    public double AvgAiHours     { get; set; }
    public double AvgActualHours { get; set; }
}

/// <summary>T55: Token usage per model for cost monitoring.</summary>
public class AiTokenUsageRow
{
    public string Model       { get; set; } = string.Empty;
    public int    TotalCalls  { get; set; }
    public int    TotalInput  { get; set; }
    public int    TotalOutput { get; set; }

    // Gemini Flash: $0.075 / 1M input, $0.30 / 1M output
    // Gemini Pro:   $1.25  / 1M input, $5.00 / 1M output
    public decimal EstimatedCostUSD =>
        Model.Contains("flash", StringComparison.OrdinalIgnoreCase)
            ? Math.Round(TotalInput  / 1_000_000m * 0.075m
                       + TotalOutput / 1_000_000m * 0.30m, 4)
            : Math.Round(TotalInput  / 1_000_000m * 1.25m
                       + TotalOutput / 1_000_000m * 5.00m, 4);

    // BDT at ~110 per USD
    public decimal EstimatedCostBDT => Math.Round(EstimatedCostUSD * 110m, 2);
}
