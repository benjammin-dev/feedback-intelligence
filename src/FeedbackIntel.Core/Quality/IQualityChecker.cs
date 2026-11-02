namespace FeedbackIntel.Core.Quality;

/// <summary>Checks the reporting data before anything is published (FR-DAT-05).</summary>
public interface IQualityChecker
{
    /// <summary>
    /// Runs every check and returns one message per failed check, or an empty list when all
    /// pass. Messages describe the check and how many rows failed it, never the data itself,
    /// because they end up in public alert issues.
    /// </summary>
    /// <param name="runId">The current run, which the reporting data must come from.</param>
    /// <param name="now">The current time, for the freshness checks.</param>
    /// <param name="cancellationToken">Stops the checks.</param>
    Task<IReadOnlyList<string>> FindProblemsAsync(long runId, DateTime now, CancellationToken cancellationToken);
}
