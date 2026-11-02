using FeedbackIntel.Core.Configuration;
using FeedbackIntel.Core.Orchestration;

namespace FeedbackIntel.Core.Monitoring;

/// <summary>
/// Runs after history is restored and reports when the last successful run is older than
/// the alert threshold (FR-OPS-02, NFR-REL-02). Stale data is a reason to alert, not to stop:
/// this run is the one that can fix it, so the run carries on.
/// </summary>
public sealed class FreshnessCheckStep(
    IRunStore runStore, IStaleDataReporter reporter, OperationsOptions options, IClock clock) : IPipelineStep
{
    /// <inheritdoc />
    public string Name => "freshness check";

    /// <inheritdoc />
    public async Task ExecuteAsync(RunContext context, CancellationToken cancellationToken)
    {
        // No successful run yet means nothing has been published, so nothing can be stale.
        var lastSuccess = await runStore.GetLastSuccessfulFinishAsync(cancellationToken);
        if (lastSuccess is null)
        {
            return;
        }

        var age = clock.UtcNow - lastSuccess.Value;
        if (age > TimeSpan.FromHours(options.StaleDataAlertHours))
        {
            reporter.ReportStale(age);
        }
    }
}
