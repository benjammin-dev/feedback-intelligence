using FeedbackIntel.Core.Configuration;
using FeedbackIntel.Core.Model;
using FeedbackIntel.Core.Orchestration;

namespace FeedbackIntel.Core.Ingestion;

/// <summary>
/// Step 3: fetches issues for each enabled repository, validates them, and stores the
/// valid ones in <c>raw.issue</c>. Fails the run if too many records are invalid.
/// </summary>
/// <remarks>
/// <para>
/// A repository with a watermark fetches only issues updated since then (FR-ING-02). A new
/// repository, or one whose history was lost, fetches the last
/// <see cref="IngestionOptions.InitialHistoryDays"/> days instead.
/// </para>
/// <para>
/// A backfill (FR-ING-06) also re-fetches every issue created in its date range, so they are
/// redacted again and upserted (FR-DAT-03). The normal fetch still happens in the same run, so
/// the dashboard stays current, and the watermarks stay correct: a re-fetched issue that wasn't
/// updated since the watermark is older than it, so it can't move the watermark forward.
/// </para>
/// </remarks>
public sealed class IngestIssuesStep(
    IReadOnlyList<RepositoryOptions> repositories,
    IngestionOptions options,
    BackfillOptions backfill,
    IIssueSource source,
    IIssueStore store,
    IWatermarkStore watermarks,
    IClock clock) : IPipelineStep
{
    /// <summary>
    /// The oldest creation date a backfill may start from. Older ranges would take longer than
    /// a run is allowed, and fall outside the dashboard's longest history window.
    /// </summary>
    public const int MaxBackfillDays = 365;

    /// <inheritdoc />
    public string Name => "ingest issues";

    /// <inheritdoc />
    public async Task ExecuteAsync(RunContext context, CancellationToken cancellationToken)
    {
        var now = clock.UtcNow;
        var historyStart = now.AddDays(-options.InitialHistoryDays);
        var range = backfill.ToRange(DateOnly.FromDateTime(now));
        if (range is not null)
        {
            if (range.Start < now.Date.AddDays(-MaxBackfillDays))
            {
                throw new InvalidOperationException(
                    $"A backfill can start at most {MaxBackfillDays} days ago; this one starts on {range.From:yyyy-MM-dd}.");
            }

            context.Run.BackfillFrom = range.Start;
            context.Run.BackfillTo = range.To.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        }

        var marks = await watermarks.GetAllAsync(cancellationToken);

        await store.ClearRawIssuesAsync(cancellationToken);

        foreach (var repositoryOptions in repositories.Where(r => r.Enabled))
        {
            var repository = await store.EnsureRepositoryAsync(
                repositoryOptions.Owner, repositoryOptions.Name, now, cancellationToken);

            // GitHub's "since" includes issues updated at exactly that time, so an issue at the
            // watermark is fetched again. That's harmless: the cleaned layer upserts (FR-DAT-03).
            var since = marks.TryGetValue(repository.Id, out var mark) ? mark : historyStart;

            // Every issue created in the backfill range was updated at or after its creation, so
            // one fetch from the earlier of the two dates finds both sets; the filter keeps them.
            var fetchFrom = range is not null && range.Start < since ? range.Start : since;
            var fetched = Deduplicate(await source.FetchUpdatedSinceAsync(repository, fetchFrom, cancellationToken))
                .Where(issue => issue.UpdatedAt >= since || (range is not null && range.Contains(issue.CreatedAt)))
                .ToList();
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
