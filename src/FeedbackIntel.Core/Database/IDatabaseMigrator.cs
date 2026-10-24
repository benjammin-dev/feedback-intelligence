namespace FeedbackIntel.Core.Database;

/// <summary>Brings the database schema up to date by applying any new migration scripts.</summary>
public interface IDatabaseMigrator
{
    /// <summary>
    /// Waits for the database server to accept connections, creates the database
    /// if it doesn't exist, then applies migrations that haven't run yet.
    /// </summary>
    Task MigrateAsync(CancellationToken cancellationToken);
}
