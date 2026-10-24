using FeedbackIntel.Core.Database;
using FeedbackIntel.Core.Orchestration;

namespace FeedbackIntel.Core.Steps;

/// <summary>Step 1: bring the database schema up to date before anything uses it.</summary>
public sealed class MigrateDatabaseStep(IDatabaseMigrator migrator) : IPipelineStep
{
    /// <inheritdoc />
    public string Name => "migrate database";

    /// <inheritdoc />
    public Task ExecuteAsync(RunContext context, CancellationToken cancellationToken) =>
        migrator.MigrateAsync(cancellationToken);
}
