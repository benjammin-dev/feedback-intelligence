namespace FeedbackIntel.Core.Orchestration;

/// <summary>Saves and reads run records in <c>ops.run</c>.</summary>
public interface IRunStore
{
    /// <summary>Inserts or updates the record for this run.</summary>
    Task SaveAsync(RunRecord run, CancellationToken cancellationToken);

    /// <summary>When the most recent successful run finished (UTC), or null if none has.</summary>
    Task<DateTime?> GetLastSuccessfulFinishAsync(CancellationToken cancellationToken);
}
