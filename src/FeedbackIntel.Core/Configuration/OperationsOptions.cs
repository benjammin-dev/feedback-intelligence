namespace FeedbackIntel.Core.Configuration;

/// <summary>The <c>Operations</c> section of <c>config/pipeline.json</c>.</summary>
public sealed class OperationsOptions
{
    /// <summary>How long to wait for SQL Server to accept connections when the run starts.</summary>
    public int SqlStartupWaitSeconds { get; set; } = 120;

    /// <summary>
    /// How old the published data may get, in hours, before the run reports it as stale
    /// (FR-OPS-02, NFR-REL-02).
    /// </summary>
    public int StaleDataAlertHours { get; set; } = 48;

    /// <summary>
    /// True only for a deliberate first run with no history yet, set by the workflow's
    /// <c>bootstrap</c> input. Normally a missing history fails the run, because silently
    /// starting over would lose every issue seen so far.
    /// </summary>
    public bool Bootstrap { get; set; }
}
