using FeedbackIntel.Core.Configuration;
using FeedbackIntel.Core.Model;
using FeedbackIntel.Core.Orchestration;

namespace FeedbackIntel.Core.Ingestion;

/// <summary>
/// Step 3: fetches issues for each enabled repository, validates them, and stores the
/// valid ones in <c>raw.issue</c>. Fails the run if too many records are invalid.
/// </summary>
/// <remarks>
/// A repository with a watermark fetches only issues updated since then (FR-ING-02). A new
/// repository, or one whose history was lost, fetches the last
/// <see cref="IngestionOptions.InitialHistoryDays"/> days instead.
/// </remarks>
public sealed class IngestIssuesStep(
    IReadOnlyList<RepositoryOptions> repositories,
    IngestionOptions options,
    IIssueSource source,
    IIssueStore store,
    IWatermarkStore watermarks,
    IClock clock) : IPipelineStep
{
    /// <inheritdoc />
    public string Name => "ingest issues";

    /// <inheritdoc />
    public async Task ExecuteAsync(RunContext context, CancellationToken cancellationToken)
    {
        var now = clock.UtcNow;
        var historyStart = now.AddDays(-options.InitialHistoryDays);
        var marks = await watermarks.GetAllAsync(cancellationToken);

        await store.ClearRawIssuesAsync(cancellationToken);

        foreach (var repositoryOptions in repositories.Where(r => r.Enabled))
        {
            var repository = await store.EnsureRepositoryAsync(
                repositoryOptions.Owner, repositoryOptions.Name, now, cancellationToken);

            // GitHub's "since" includes issues updated at exactly that time, so an issue at the
            // watermark is fetched again. That's harmless: the cleaned layer upserts (FR-DAT-03).
            var since = marks.TryGetValue(repository.Id, out var mark) ? mark : historyStart;
            var fetched = Deduplicate(await source.FetchUpdatedSinceAsync(repository, since, cancellationToken));
            var valid = fetched.Where(issue => RawIssueValidator.FindProblem(issue) is null).ToList();

            await store.InsertRawIssuesAsync(valid, cancellationToken);

            context.Run.IssuesFetched += fetched.Count;
            context.Run.IssuesRejected += fetched.Count - valid.Count;
        }

        ThrowIfTooManyRejected(context.Run);
    }

    /// <summary>
    /// GitHub can return the same issue twice if it changes while pages are being read.
    /// Keeps the most recently updated copy, since two rows would break the primary key.
    /// </summary>
    private static List<RawIssue> Deduplicate(IEnumerable<RawIssue> issues) =>
        issues
            .GroupBy(issue => issue.IssueNumber)
            .Select(group => group.MaxBy(issue => issue.UpdatedAt)!)
            .ToList();

    /// <remarks>
    /// Only applies once at least <see cref="IngestionOptions.MinIssuesForRejectionCheck"/> issues
    /// were fetched. Incremental runs often fetch only a few, and one odd issue out of five
    /// would otherwise look like a 20% failure rate and stop the run every day.
    /// </remarks>
    private void ThrowIfTooManyRejected(RunRecord run)
    {
        if (run.IssuesFetched >= options.MinIssuesForRejectionCheck
            && run.IssuesRejected * 100L > run.IssuesFetched * (long)options.MaxRejectedPercent)
        {
            throw new InvalidDataException(
                $"{run.IssuesRejected} of {run.IssuesFetched} fetched issues failed validation, " +
                $"above the {options.MaxRejectedPercent}% limit. The GitHub API may have changed.");
        }
    }
}
