using FeedbackIntel.Core.Ingestion;
using FeedbackIntel.Core.Orchestration;

namespace FeedbackIntel.Core.Tests.Ingestion;

public class AdvanceWatermarksStepTests
{
    [Fact]
    public async Task ExecuteAsync_AdvancesWithThisRunsId()
    {
        var store = new RecordingWatermarkStore();
        var context = new RunContext(new RunRecord { RunId = 987, Trigger = "schedule", StartedAt = TestIssues.Created });

        await new AdvanceWatermarksStep(store).ExecuteAsync(context, CancellationToken.None);

        Assert.Equal(987, Assert.Single(store.AdvancedRunIds));
    }

    private sealed class RecordingWatermarkStore : IWatermarkStore
    {
        public List<long> AdvancedRunIds { get; } = [];

        public Task<IReadOnlyDictionary<short, DateTime>> GetAllAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyDictionary<short, DateTime>>(new Dictionary<short, DateTime>());

        public Task AdvanceAsync(long runId, CancellationToken cancellationToken)
        {
            AdvancedRunIds.Add(runId);
            return Task.CompletedTask;
        }
    }
}
