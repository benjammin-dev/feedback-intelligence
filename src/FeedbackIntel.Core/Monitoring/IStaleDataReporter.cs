namespace FeedbackIntel.Core.Monitoring;

/// <summary>
/// Passes on that the published data is older than it should be. The pipeline doesn't open
/// alert issues itself; it reports what it found, and the workflow's alert job, the only part
/// allowed to write issues, raises the alert (NFR-SEC-02).
/// </summary>
public interface IStaleDataReporter
{
    /// <summary>Reports that the last successful run finished this long ago.</summary>
    void ReportStale(TimeSpan dataAge);
}
