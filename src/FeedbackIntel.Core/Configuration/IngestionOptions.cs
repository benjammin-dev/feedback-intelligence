namespace FeedbackIntel.Core.Configuration;

/// <summary>The <c>Ingestion</c> section of <c>config/pipeline.json</c>.</summary>
public sealed class IngestionOptions
{
    /// <summary>How many days of issue history to fetch when there is no earlier data (FR-ING-02).</summary>
    public int InitialHistoryDays { get; set; } = 90;

    /// <summary>
    /// The highest share of fetched issues that may fail validation before the run fails.
    /// More than this suggests the GitHub API changed (risk R7).
    /// </summary>
    public int MaxRejectedPercent { get; set; } = 10;

    /// <summary>The longest the pipeline waits for a GitHub rate-limit reset.</summary>
    public int RateLimitMaxWaitMinutes { get; set; } = 15;
}
