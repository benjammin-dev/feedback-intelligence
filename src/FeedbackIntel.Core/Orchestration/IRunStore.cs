namespace FeedbackIntel.Core.Orchestration;

/// <summary>Saves run records to <c>ops.run</c>.</summary>
public interface IRunStore
{
    /// <summary>Inserts or updates the record for this run.</summary>
    Task SaveAsync(RunRecord run, CancellationToken cancellationToken);
}
