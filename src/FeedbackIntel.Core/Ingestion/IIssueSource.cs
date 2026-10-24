using FeedbackIntel.Core.Model;

namespace FeedbackIntel.Core.Ingestion;

/// <summary>Where issues come from: GitHub in production, a fake in tests.</summary>
public interface IIssueSource
{
    /// <summary>
    /// Fetches every issue in the repository updated at or after <paramref name="since"/>.
    /// Pull requests are excluded, and no usernames are returned.
    /// </summary>
    Task<IReadOnlyList<RawIssue>> FetchUpdatedSinceAsync(
        TrackedRepository repository, DateTime since, CancellationToken cancellationToken);
}
