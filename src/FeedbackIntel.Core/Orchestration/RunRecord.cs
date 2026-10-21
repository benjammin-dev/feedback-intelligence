namespace FeedbackIntel.Core.Orchestration;

/// <summary>
/// The logbook entry for one pipeline run. Mirrors the <c>ops.run</c> table
/// in the data dictionary. Steps update the counters as they work.
/// </summary>
public sealed class RunRecord
{
    /// <summary>The GitHub Actions run ID, so the record links to that run's logs.</summary>
    public required long RunId { get; init; }

    /// <summary><c>schedule</c> or <c>manual</c>.</summary>
    public required string Trigger { get; init; }

    /// <summary>When the run began (UTC).</summary>
    public required DateTime StartedAt { get; init; }

    /// <summary>When the run ended (UTC), or null while running.</summary>
    public DateTime? FinishedAt { get; set; }

    /// <summary>Whether the run is running, succeeded, or failed.</summary>
    public RunStatus Status { get; set; } = RunStatus.Running;

    /// <summary>How many issues were fetched from GitHub.</summary>
    public int IssuesFetched { get; set; }

    /// <summary>How many fetched records failed validation and were skipped.</summary>
    public int IssuesRejected { get; set; }

    /// <summary>How many issues passed through redaction.</summary>
    public int IssuesRedacted { get; set; }

    /// <summary>How many AI requests were sent.</summary>
    public short AiRequests { get; set; }

    /// <summary>How many issues reused cached AI results.</summary>
    public int AiCacheHits { get; set; }

    /// <summary>How many issues the AI could not classify.</summary>
    public int IssuesUnclassified { get; set; }

    /// <summary>The name of the step that failed, or null on success.</summary>
    public string? FailedStep { get; set; }

    /// <summary>
    /// The error text, or null on success. Must contain only system details,
    /// never issue content, because it is saved to public history.
    /// </summary>
    public string? ErrorMessage { get; set; }
}
