namespace FeedbackIntel.Core.Ingestion;

/// <summary>
/// Tracks how far ingestion has reached for each repository, in <c>ops.watermark</c>,
/// so each run fetches only what changed since the last successful one (FR-ING-02).
/// </summary>
public interface IWatermarkStore
{
    /// <summary>Returns each repository's watermark, keyed by repository ID.</summary>
    Task<IReadOnlyDictionary<short, DateTime>> GetAllAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Moves each repository's watermark forward to the newest update time fetched this run.
    /// A watermark never moves backward.
    /// </summary>
    Task AdvanceAsync(long runId, CancellationToken cancellationToken);
}
