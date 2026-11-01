namespace FeedbackIntel.Core.History;

/// <summary>
/// Saves the system's long-term memory between runs and restores it at the start of the
/// next one (FR-DAT-02, FR-DAT-06). The runner's database is temporary, so without this
/// every run would start from nothing (ADR 0005).
/// </summary>
public interface IHistoryStore
{
    /// <summary>True if history files from an earlier run are available to restore.</summary>
    bool HistoryExists();

    /// <summary>
    /// True if the database already holds data. That happens locally, where the database
    /// persists between runs; on GitHub's runner it never does.
    /// </summary>
    Task<bool> DatabaseHasDataAsync(CancellationToken cancellationToken);

    /// <summary>Loads the history files into the database.</summary>
    Task RestoreAsync(CancellationToken cancellationToken);

    /// <summary>Writes the history files from the database, replacing earlier versions.</summary>
    Task SaveAsync(CancellationToken cancellationToken);
}
