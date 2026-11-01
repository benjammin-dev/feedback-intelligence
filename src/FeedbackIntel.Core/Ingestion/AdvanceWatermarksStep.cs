using FeedbackIntel.Core.Orchestration;

namespace FeedbackIntel.Core.Ingestion;

/// <summary>
/// The last step: moves the watermarks forward. It runs only after everything else has
/// succeeded, including publishing, so a failed run leaves the watermarks where they were
/// and the next run fetches the missed issues again (design: a failed run is never harmful).
/// </summary>
public sealed class AdvanceWatermarksStep(IWatermarkStore watermarks) : IPipelineStep
{
    /// <inheritdoc />
    public string Name => "advance watermarks";

    /// <inheritdoc />
    public Task ExecuteAsync(RunContext context, CancellationToken cancellationToken) =>
        watermarks.AdvanceAsync(context.Run.RunId, cancellationToken);
}
