using FeedbackIntel.Core.Configuration;
using FeedbackIntel.Core.Model;
using FeedbackIntel.Core.Orchestration;

namespace FeedbackIntel.Core.Ingestion;

/// <summary>
/// Step 3: fetches recent issues for each enabled repository, validates them, and stores
/// the valid ones in <c>raw.issue</c>. Fails the run if too many records are invalid.
/// </summary>
/// <remarks>
/// For the walking skeleton this always fetches the last <see cref="IngestionOptions.InitialHistoryDays"/>.
/// M3 replaces that with a watermark, so each run fetches only what changed since the last one.
/// </remarks>
public sealed class IngestIssuesStep(
    IReadOnlyList<RepositoryOptions> repositories,
    IngestionOptions options,
    IIssueSource source,
    IIssueStore store,
    IClock clock) : IPipelineStep
{
    /// <inheritdoc />
    public string Name => "ingest issues";

    /// <inheritdoc />
    public async Task ExecuteAsync(RunContext context, CancellationToken cancellationToken)
    {
        var now = clock.UtcNow;
        var since = now.AddDays(-options.InitialHistoryDays);

        await store.ClearRawIssuesAsync(cancellationToken);

        foreach (var repositoryOptions in repositories.Where(r => r.Enabled))
        {
            var repository = await store.EnsureRepositoryAsync(
                repositoryOptions.Owner, repositoryOptions.Name, now, cancellationToken);

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

    private void ThrowIfTooManyRejected(RunRecord run)
    {
        if (run.IssuesFetched > 0 && run.IssuesRejected * 100L > run.IssuesFetched * (long)options.MaxRejectedPercent)
        {
            throw new InvalidDataException(
                $"{run.IssuesRejected} of {run.IssuesFetched} fetched issues failed validation, " +
                $"above the {options.MaxRejectedPercent}% limit. The GitHub API may have changed.");
        }
    }
}
