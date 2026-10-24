using FeedbackIntel.Core.Orchestration;

namespace FeedbackIntel.Core.Reporting;

/// <summary>
/// Step 6: rebuilds the reporting tables. Uses the run's start time as "now", so every
/// number in one run is measured against the same moment.
/// </summary>
public sealed class RebuildReportingStep(IReportingBuilder builder) : IPipelineStep
{
    /// <inheritdoc />
    public string Name => "rebuild reporting";

    /// <inheritdoc />
    public Task ExecuteAsync(RunContext context, CancellationToken cancellationToken) =>
        builder.RebuildAsync(context.Run.StartedAt, context.Run.RunId, cancellationToken);
}
