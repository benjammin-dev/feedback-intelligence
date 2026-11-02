using FeedbackIntel.Core.Orchestration;

namespace FeedbackIntel.Core.Privacy;

/// <summary>
/// Runs just before redaction: sends the known PII test set through the live redaction
/// service and stops the run if any value survives, so a misbehaving service can never
/// publish real issues unredacted (FR-PRV-04, NFR-PRIV-01). The samples are made up, so the
/// check is safe to run every day and can't fail because of unusual real data.
/// </summary>
public sealed class PrivacyCheckStep(IPiiRedactor redactor) : IPipelineStep
{
    /// <inheritdoc />
    public string Name => "privacy check";

    /// <inheritdoc />
    public async Task ExecuteAsync(RunContext context, CancellationToken cancellationToken)
    {
        var missed = new List<string>();
        foreach (var sample in PiiTestSet.Samples)
        {
            var result = await redactor.RedactAsync(sample.Text, cancellationToken);
            if (result.Text.Contains(sample.Secret, StringComparison.Ordinal))
            {
                missed.Add(sample.Placeholder.Trim('<', '>'));
            }
        }

        if (missed.Count > 0)
        {
            // Only the kinds of value are named, never the values, even though they're made up.
            throw new InvalidOperationException(
                $"The redaction service missed {missed.Count} of {PiiTestSet.Samples.Count} known samples " +
                $"({string.Join(", ", missed.Distinct())}). Nothing was redacted or published.");
        }
    }
}
