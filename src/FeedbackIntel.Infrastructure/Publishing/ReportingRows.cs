using System.Text.Json.Serialization;

namespace FeedbackIntel.Infrastructure.Publishing;

// One class per published Parquet file. Each property's JsonPropertyName is its column name
// in both SQL Server and the file. Only these columns are ever published (NFR-PRIV-02).

/// <summary>A row of <c>issues.parquet</c>: all of <c>reporting.issue</c>.</summary>
public sealed class IssueReportRow
{
    [JsonPropertyName("repository")]
    public string Repository { get; set; } = "";

    [JsonPropertyName("issue_number")]
    public int IssueNumber { get; set; }

    /// <summary>The redacted title. The only issue text published apart from the AI summary.</summary>
    [JsonPropertyName("title")]
    public string Title { get; set; } = "";

    [JsonPropertyName("state")]
    public string State { get; set; } = "";

    [JsonPropertyName("state_reason")]
    public string? StateReason { get; set; }

    [JsonPropertyName("created_at")]
    public DateTime CreatedAt { get; set; }

    [JsonPropertyName("updated_at")]
    public DateTime UpdatedAt { get; set; }

    [JsonPropertyName("closed_at")]
    public DateTime? ClosedAt { get; set; }

    [JsonPropertyName("days_to_close")]
    public decimal? DaysToClose { get; set; }

    [JsonPropertyName("category")]
    public string? Category { get; set; }

    [JsonPropertyName("severity")]
    public string? Severity { get; set; }

    [JsonPropertyName("severity_reason")]
    public string? SeverityReason { get; set; }

    [JsonPropertyName("summary")]
    public string? Summary { get; set; }

    [JsonPropertyName("ai_status")]
    public string AiStatus { get; set; } = "";

    [JsonPropertyName("html_url")]
    public string HtmlUrl { get; set; } = "";
}

/// <summary>A row of <c>weekly_metrics.parquet</c>: all of <c>reporting.weekly_metrics</c>.</summary>
public sealed class WeeklyMetricsRow
{
    [JsonPropertyName("repository")]
    public string Repository { get; set; } = "";

    [JsonPropertyName("week_start")]
    public DateTime WeekStart { get; set; }

    [JsonPropertyName("new_issues")]
    public int NewIssues { get; set; }

    [JsonPropertyName("closed_issues")]
    public int ClosedIssues { get; set; }

    [JsonPropertyName("critical_count")]
    public int? CriticalCount { get; set; }

    [JsonPropertyName("median_days_to_close")]
    public decimal? MedianDaysToClose { get; set; }
}

/// <summary>A row of <c>run_health.parquet</c>: all of <c>reporting.run_health</c>.</summary>
public sealed class RunHealthRow
{
    [JsonPropertyName("run_id")]
    public long RunId { get; set; }

    [JsonPropertyName("trigger")]
    public string Trigger { get; set; } = "";

    [JsonPropertyName("started_at")]
    public DateTime StartedAt { get; set; }

    [JsonPropertyName("duration_seconds")]
    public int? DurationSeconds { get; set; }

    [JsonPropertyName("status")]
    public string Status { get; set; } = "";

    [JsonPropertyName("issues_fetched")]
    public int IssuesFetched { get; set; }

    [JsonPropertyName("ai_requests")]
    public short AiRequests { get; set; }

    [JsonPropertyName("ai_cache_hits")]
    public int AiCacheHits { get; set; }

    [JsonPropertyName("issues_unclassified")]
    public int IssuesUnclassified { get; set; }

    [JsonPropertyName("failed_step")]
    public string? FailedStep { get; set; }
}
