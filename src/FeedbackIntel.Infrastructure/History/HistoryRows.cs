using System.Text.Json.Serialization;

namespace FeedbackIntel.Infrastructure.History;

// One class per history file. Each property's JsonPropertyName is its column name in both
// SQL Server and the Parquet file, which keeps the files matching the data contract.
// Only the columns listed here are ever saved.

/// <summary>A row of <c>repositories.parquet</c>: all of <c>cleaned.repository</c>.</summary>
public sealed class RepositoryRow
{
    [JsonPropertyName("repo_id")]
    public short RepoId { get; set; }

    [JsonPropertyName("owner")]
    public string Owner { get; set; } = "";

    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("onboarded_at")]
    public DateTime OnboardedAt { get; set; }
}

/// <summary>
/// A row of <c>issues.parquet</c>: <c>cleaned.issue</c> without <c>body_redacted</c>.
/// Issue bodies never leave a run (ADR 0013), so there is deliberately no property for it.
/// </summary>
public sealed class IssueRow
{
    [JsonPropertyName("repo_id")]
    public short RepoId { get; set; }

    [JsonPropertyName("issue_number")]
    public int IssueNumber { get; set; }

    [JsonPropertyName("title_redacted")]
    public string TitleRedacted { get; set; } = "";

    [JsonPropertyName("body_length")]
    public int BodyLength { get; set; }

    [JsonPropertyName("body_truncated")]
    public bool BodyTruncated { get; set; }

    [JsonPropertyName("content_hash")]
    public byte[] ContentHash { get; set; } = [];

    [JsonPropertyName("redaction_count")]
    public short RedactionCount { get; set; }

    [JsonPropertyName("state")]
    public string State { get; set; } = "";

    [JsonPropertyName("state_reason")]
    public string? StateReason { get; set; }

    [JsonPropertyName("labels")]
    public string Labels { get; set; } = "[]";

    [JsonPropertyName("created_at")]
    public DateTime CreatedAt { get; set; }

    [JsonPropertyName("updated_at")]
    public DateTime UpdatedAt { get; set; }

    [JsonPropertyName("closed_at")]
    public DateTime? ClosedAt { get; set; }

    [JsonPropertyName("html_url")]
    public string HtmlUrl { get; set; } = "";

    [JsonPropertyName("first_seen_run_id")]
    public long FirstSeenRunId { get; set; }

    [JsonPropertyName("last_changed_run_id")]
    public long LastChangedRunId { get; set; }
}

/// <summary>A row of <c>runs.parquet</c>: all of <c>ops.run</c>.</summary>
public sealed class RunRow
{
    [JsonPropertyName("run_id")]
    public long RunId { get; set; }

    [JsonPropertyName("trigger")]
    public string Trigger { get; set; } = "";

    [JsonPropertyName("backfill_from")]
    public DateTime? BackfillFrom { get; set; }

    [JsonPropertyName("backfill_to")]
    public DateTime? BackfillTo { get; set; }

    [JsonPropertyName("started_at")]
    public DateTime StartedAt { get; set; }

    [JsonPropertyName("finished_at")]
    public DateTime? FinishedAt { get; set; }

    [JsonPropertyName("status")]
    public string Status { get; set; } = "";

    [JsonPropertyName("issues_fetched")]
    public int IssuesFetched { get; set; }

    [JsonPropertyName("issues_rejected")]
    public int IssuesRejected { get; set; }

    [JsonPropertyName("issues_redacted")]
    public int IssuesRedacted { get; set; }

    [JsonPropertyName("ai_requests")]
    public short AiRequests { get; set; }

    [JsonPropertyName("ai_cache_hits")]
    public int AiCacheHits { get; set; }

    [JsonPropertyName("issues_unclassified")]
    public int IssuesUnclassified { get; set; }

    [JsonPropertyName("failed_step")]
    public string? FailedStep { get; set; }

    [JsonPropertyName("error_message")]
    public string? ErrorMessage { get; set; }
}

/// <summary>A row of <c>watermarks.parquet</c>: all of <c>ops.watermark</c>.</summary>
public sealed class WatermarkRow
{
    [JsonPropertyName("repo_id")]
    public short RepoId { get; set; }

    [JsonPropertyName("last_updated_at")]
    public DateTime LastUpdatedAt { get; set; }

    [JsonPropertyName("last_success_run_id")]
    public long LastSuccessRunId { get; set; }
}
