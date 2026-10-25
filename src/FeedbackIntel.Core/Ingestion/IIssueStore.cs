using FeedbackIntel.Core.Model;

namespace FeedbackIntel.Core.Ingestion;

/// <summary>Stores repositories and fetched issues in the database.</summary>
public interface IIssueStore
{
    /// <summary>Returns the repository's record, adding it first if it isn't tracked yet.</summary>
    Task<TrackedRepository> EnsureRepositoryAsync(
        string owner, string name, DateTime onboardedAt, CancellationToken cancellationToken);

    /// <summary>Empties <c>raw.issue</c>, so it only ever holds the current run's data.</summary>
    Task ClearRawIssuesAsync(CancellationToken cancellationToken);

    /// <summary>Adds issues to <c>raw.issue</c>.</summary>
    Task InsertRawIssuesAsync(IReadOnlyCollection<RawIssue> issues, CancellationToken cancellationToken);

    /// <summary>Reads every issue in <c>raw.issue</c>, which holds only this run's fetch.</summary>
    Task<IReadOnlyList<RawIssue>> ReadRawIssuesAsync(CancellationToken cancellationToken);
}
