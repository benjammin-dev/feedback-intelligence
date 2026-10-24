using FeedbackIntel.Core.Database;
using FeedbackIntel.Core.Orchestration;

namespace FeedbackIntel.Core.Tests.Database;

public class MigrateDatabaseStepTests
{
    [Fact]
    public async Task ExecuteAsync_CallsMigratorOnce()
    {
        var migrator = new CountingMigrator();
        var step = new MigrateDatabaseStep(migrator);
        var context = new RunContext(new RunRecord { RunId = 1, Trigger = "manual", StartedAt = DateTime.UtcNow });

        await step.ExecuteAsync(context, CancellationToken.None);

        Assert.Equal(1, migrator.Calls);
    }

    private sealed class CountingMigrator : IDatabaseMigrator
    {
        public int Calls { get; private set; }

        public Task MigrateAsync(CancellationToken cancellationToken)
        {
            Calls++;
            return Task.CompletedTask;
        }
    }
}
