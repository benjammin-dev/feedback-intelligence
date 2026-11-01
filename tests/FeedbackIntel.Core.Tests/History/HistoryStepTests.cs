using FeedbackIntel.Core.Configuration;
using FeedbackIntel.Core.History;
using FeedbackIntel.Core.Orchestration;
using FeedbackIntel.Core.Tests.Fakes;

namespace FeedbackIntel.Core.Tests.History;

public class HistoryStepTests
{
    private static readonly DateTime Start = new(2026, 11, 1, 13, 17, 0, DateTimeKind.Utc);

    private readonly FakeHistoryStore _history = new();

    [Fact]
    public async Task Restore_HistoryExists_RestoresIt()
    {
        _history.Exists = true;

        await Restore(bootstrap: false);

        Assert.Equal<string>(["restore"], _history.Operations);
    }

    [Fact]
    public async Task Restore_DatabaseAlreadyHasData_SkipsRestore()
    {
        _history.Exists = true;
        _history.DatabaseHasData = true;

        await Restore(bootstrap: false);

        Assert.Empty(_history.Operations);
    }

    [Fact]
    public async Task Restore_NoHistoryWithoutBootstrap_FailsTheRun()
    {
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => Restore(bootstrap: false));

        Assert.Contains("bootstrap", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Restore_NoHistoryWithBootstrap_StartsEmpty()
    {
        await Restore(bootstrap: true);

        Assert.Empty(_history.Operations);
    }

    [Fact]
    public async Task Save_RecordsThisRunAsSucceededBeforeSavingHistory()
    {
        var runs = new FakeRunStore();
        var clock = new FakeClock(Start.AddMinutes(2));
        var context = Context();
        context.Run.IssuesFetched = 12;
        var step = new SaveHistoryStep(_history, runs, clock);

        await step.ExecuteAsync(context, CancellationToken.None);

        var saved = Assert.Single(runs.Saved);
        Assert.Equal(RunStatus.Succeeded, saved.Status);
        Assert.Equal(Start.AddMinutes(2), saved.FinishedAt);
        Assert.Equal(12, saved.IssuesFetched);
        Assert.Equal<string>(["save"], _history.Operations);
        Assert.Equal(RunStatus.Running, context.Run.Status);
    }

    private Task Restore(bool bootstrap) =>
        new RestoreHistoryStep(_history, new OperationsOptions { Bootstrap = bootstrap })
            .ExecuteAsync(Context(), CancellationToken.None);

    private static RunContext Context() =>
        new(new RunRecord { RunId = 1, Trigger = "schedule", StartedAt = Start });

    private sealed class FakeHistoryStore : IHistoryStore
    {
        public bool Exists { get; set; }

        public bool DatabaseHasData { get; set; }

        public List<string> Operations { get; } = [];

        public bool HistoryExists() => Exists;

        public Task<bool> DatabaseHasDataAsync(CancellationToken cancellationToken) => Task.FromResult(DatabaseHasData);

        public Task RestoreAsync(CancellationToken cancellationToken)
        {
            Operations.Add("restore");
            return Task.CompletedTask;
        }

        public Task SaveAsync(CancellationToken cancellationToken)
        {
            Operations.Add("save");
            return Task.CompletedTask;
        }
    }
}
