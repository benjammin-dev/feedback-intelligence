using System.Globalization;

using FeedbackIntel.Core.Monitoring;
using FeedbackIntel.Core.Orchestration;

using Microsoft.Extensions.Logging;

namespace FeedbackIntel.Infrastructure.Actions;

/// <summary>
/// Hands the run's result to the workflow's alert job through step outputs (the file named by
/// <c>GITHUB_OUTPUT</c>). The alert job, the only part allowed to write issues, opens or
/// updates the alert issue from them (FR-OPS-02, NFR-SEC-02). Outside GitHub Actions there is
/// no output file, and the result is only logged.
/// </summary>
/// <param name="outputPath">The path in <c>GITHUB_OUTPUT</c>, or null when not running in Actions.</param>
/// <param name="logger">Logs stale data, so local runs see it too.</param>
public sealed class GitHubActionsOutput(string? outputPath, ILogger<GitHubActionsOutput> logger) : IStaleDataReporter
{
    /// <summary>The longest error passed on. The full text stays in the run record.</summary>
    public const int MaxErrorLength = 500;

    /// <inheritdoc />
    public void ReportStale(TimeSpan dataAge)
    {
        var hours = (int)dataAge.TotalHours;
        logger.LogWarning("Data is stale: the last successful run finished {Hours} hours ago", hours);
        Write("stale_hours", hours.ToString(CultureInfo.InvariantCulture));
    }

    /// <summary>Passes on which step failed and why. Does nothing for a successful run.</summary>
    public void WriteRunResult(RunRecord run)
    {
        if (run.Status != RunStatus.Failed)
        {
            return;
        }

        Write("failed_step", run.FailedStep ?? "unknown");
        Write("error", ToSingleLine(run.ErrorMessage ?? "", MaxErrorLength));
    }

    /// <summary>
    /// Makes text safe for a one-line output and for the code block it is shown in: no line
    /// breaks, no backticks that could end the block early, and no more than the given length.
    /// </summary>
    internal static string ToSingleLine(string text, int maxLength)
    {
        var cleaned = string.Join(' ', text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)).Replace('`', '\'');
        return cleaned.Length <= maxLength ? cleaned : cleaned[..(maxLength - 3)] + "...";
    }

    private void Write(string name, string value)
    {
        if (string.IsNullOrEmpty(outputPath))
        {
            return;
        }

        File.AppendAllText(outputPath, $"{name}={value}\n");
    }
}
