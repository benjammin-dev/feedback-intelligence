using System.Diagnostics;

using FeedbackIntel.Core.Orchestration;

using Microsoft.Extensions.Logging;

namespace FeedbackIntel.Pipeline;

/// <summary>
/// Wraps a step to log when it starts, finishes, or fails, inside a scope that tags every
/// log line with the step name (NFR-OBS-01). This is the decorator pattern: Core's steps
/// stay free of logging code.
/// </summary>
/// <remarks>
/// Only the exception type is logged here, never its message, because run logs are public
/// and a message could contain data. The orchestrator records the message in the run record.
/// </remarks>
internal sealed class LoggingStep(IPipelineStep inner, ILogger logger) : IPipelineStep
{
    public string Name => inner.Name;

    public async Task ExecuteAsync(RunContext context, CancellationToken cancellationToken)
    {
        using var scope = logger.BeginScope("Step {Step}", inner.Name);
        var stopwatch = Stopwatch.StartNew();
        logger.LogInformation("Started");

        try
        {
            await inner.ExecuteAsync(context, cancellationToken);
            logger.LogInformation("Finished in {Seconds:F1}s", stopwatch.Elapsed.TotalSeconds);
        }
        catch (Exception ex)
        {
            logger.LogError("Failed after {Seconds:F1}s with {ErrorType}", stopwatch.Elapsed.TotalSeconds, ex.GetType().Name);
            throw;
        }
    }
}
