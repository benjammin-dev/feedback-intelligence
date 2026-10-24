namespace FeedbackIntel.Core.Reporting;

/// <summary>Rebuilds the reporting layer, the only data the publisher reads.</summary>
public interface IReportingBuilder
{
    /// <summary>Recalculates every reporting table as of the given time.</summary>
    Task RebuildAsync(DateTime asOf, long runId, CancellationToken cancellationToken);
}
