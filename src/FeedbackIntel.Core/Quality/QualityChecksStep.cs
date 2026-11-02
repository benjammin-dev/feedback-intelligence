using FeedbackIntel.Core.Orchestration;

namespace FeedbackIntel.Core.Quality;

/// <summary>
/// Step 8: stops the run before publishing if the reporting data looks wrong, so the
/// dashboard keeps showing the last good data (FR-DAT-05, FR-OPS-05). Every failed check is
/// listed, not just the first, so one alert explains the whole problem.
/// </summary>
public sealed class QualityChecksStep(IQualityChecker checker, IClock clock) : IPipelineStep
{
    /// <inheritdoc />
    public string Name => "quality checks";

    /// <inheritdoc />
    public async Task ExecuteAsync(RunContext context, CancellationToken cancellationToken)
    {
        var problems = await checker.FindProblemsAsync(context.Run.RunId, clock.UtcNow, cancellationToken);
        if (problems.Count > 0)
        {
            throw new InvalidDataException(
                $"{problems.Count} quality check(s) failed: " + string.Join(" ", problems));
        }
    }
}
