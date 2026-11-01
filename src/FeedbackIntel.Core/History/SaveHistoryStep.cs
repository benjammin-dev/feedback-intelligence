using FeedbackIntel.Core.Orchestration;

namespace FeedbackIntel.Core.History;

/// <summary>
/// Step 11, the last: writes the history files (FR-DAT-06). This run's own record is saved
/// first, marked as succeeded, so the history includes it.
/// </summary>
public sealed class SaveHistoryStep(IHistoryStore history, IRunStore runs, IClock clock) : IPipelineStep
{
    /// <inheritdoc />
    public string Name => "save history";

    /// <inheritdoc />
    public async Task ExecuteAsync(RunContext context, CancellationToken cancellationToken)
    {
        await runs.SaveAsync(context.Run.AsSucceeded(clock.UtcNow), cancellationToken);
        await history.SaveAsync(cancellationToken);
    }
}
