using FeedbackIntel.Core.Model;

namespace FeedbackIntel.Core.Privacy;

/// <summary>Stores redacted issues in <c>cleaned.issue</c>.</summary>
public interface ICleanedIssueStore
{
    /// <summary>
    /// Inserts new issues and updates changed ones, so reprocessing never creates
    /// duplicates (FR-DAT-03). Records which run first stored and last changed each issue.
    /// </summary>
    Task UpsertAsync(IReadOnlyCollection<CleanedIssue> issues, long runId, CancellationToken cancellationToken);
}
