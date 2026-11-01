using FeedbackIntel.Core.Configuration;
using FeedbackIntel.Core.Orchestration;

namespace FeedbackIntel.Core.History;

/// <summary>
/// Step 2: loads history into the empty database, so issues, watermarks, and run records
/// carry over from earlier runs (FR-DAT-02).
/// </summary>
public sealed class RestoreHistoryStep(IHistoryStore history, OperationsOptions options) : IPipelineStep
{
    /// <inheritdoc />
    public string Name => "restore history";

    /// <inheritdoc />
    public async Task ExecuteAsync(RunContext context, CancellationToken cancellationToken)
    {
        // Locally the database keeps its data between runs, so there's nothing to restore.
        if (await history.DatabaseHasDataAsync(cancellationToken))
        {
            return;
        }

        if (history.HistoryExists())
        {
            await history.RestoreAsync(cancellationToken);
            return;
        }

        // Starting over silently would lose every issue seen so far, so it has to be deliberate.
        if (!options.Bootstrap)
        {
            throw new InvalidOperationException(
                "No history was found to restore. If this is the very first run, start it with the bootstrap option.");
        }
    }
}
