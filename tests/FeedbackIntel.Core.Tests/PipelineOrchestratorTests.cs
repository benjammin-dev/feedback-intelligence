using FeedbackIntel.Core.Orchestration;
using FeedbackIntel.Core.Tests.Fakes;

namespace FeedbackIntel.Core.Tests;

public class PipelineOrchestratorTests
{
    private static readonly DateTime Start = new(2026, 10, 20, 6, 0, 0, DateTimeKind.Utc);

    private readonly FakeClock _clock = new(Start);
    private readonly List<string> _log = [];

    [Fact]
    public async Task RunAsync_AllStepsSucceed_RunsInOrderAndSucceeds()
    {
        var store = new FakeRunStore();
        var orchestrator = Create(store, Step("migrate"), Step("ingest"), Step("publish"));

        var run = await orchestrator.RunAsync(42, "schedule", CancellationToken.None);

        Assert.Equal<string>(["migrate", "ingest", "publish"], _log);
        Assert.Equal(RunStatus.Succeeded, run.Status);
        Assert.Null(run.FailedStep);
        Assert.Null(run.ErrorMessage);
    }

    [Fact]
    public async Task RunAsync_StepFails_StopsAndRecordsFailure()
    {
        var store = new FakeRunStore();
        var orchestrator = Create(
            store,
            Step("migrate"),
            Step("ingest", _ => throw new HttpRequestException("GitHub returned 502")),
            Step("publish"));

        var run = await orchestrator.RunAsync(42, "schedule", CancellationToken.None);

        Assert.Equal<string>(["migrate", "ingest"], _log);
        Assert.Equal(RunStatus.Failed, run.Status);
        Assert.Equal("ingest", run.FailedStep);
        Assert.Equal("HttpRequestException: GitHub returned 502", run.ErrorMessage);
    }

    [Fact]
    public async Task RunAsync_LongErrorMessage_IsTruncatedToColumnSize()
    {
        var orchestrator = Create(
            new FakeRunStore(),
            Step("ingest", _ => throw new InvalidOperationException(new string('x', 5000))));

        var run = await orchestrator.RunAsync(42, "schedule", CancellationToken.None);

        Assert.Equal(PipelineOrchestrator.MaxErrorMessageLength, run.ErrorMessage!.Length);
    }

    [Fact]
    public async Task RunAsync_RecordsRunDetailsAndTimes()
    {
        var orchestrator = Create(
            new FakeRunStore(),
            Step("ingest", _ => _clock.Advance(TimeSpan.FromMinutes(5))));

        var run = await orchestrator.RunAsync(42, "manual", CancellationToken.None);

        Assert.Equal(42, run.RunId);
        Assert.Equal("manual", run.Trigger);
        Assert.Equal(Start, run.StartedAt);
        Assert.Equal(Start.AddMinutes(5), run.FinishedAt);
    }

    [Fact]
    public async Task RunAsync_StepsCanUpdateCounters()
    {
        var orchestrator = Create(
            new FakeRunStore(),
            Step("ingest", context => context.Run.IssuesFetched = 17));

        var run = await orchestrator.RunAsync(42, "schedule", CancellationToken.None);

        Assert.Equal(17, run.IssuesFetched);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RunAsync_SuccessOrFailure_SavesRecordOnce(bool stepFails)
    {
        var store = new FakeRunStore();
        var orchestrator = Create(
            store,
            Step("ingest", _ =>
            {
                if (stepFails)
                {
                    throw new InvalidOperationException("boom");
                }
            }));

        var run = await orchestrator.RunAsync(42, "schedule", CancellationToken.None);

        Assert.Same(run, Assert.Single(store.Saved));
    }

    [Fact]
    public async Task RunAsync_SavingRecordFailsAfterSuccess_MarksRunFailed()
    {
        var orchestrator = Create(new FakeRunStore { ShouldFail = true }, Step("ingest"));

        var run = await orchestrator.RunAsync(42, "schedule", CancellationToken.None);

        Assert.Equal(RunStatus.Failed, run.Status);
        Assert.Equal(PipelineOrchestrator.SaveRunRecordStepName, run.FailedStep);
    }

    [Fact]
    public async Task RunAsync_SavingRecordFailsAfterStepFailure_KeepsOriginalError()
    {
        var orchestrator = Create(
            new FakeRunStore { ShouldFail = true },
            Step("ingest", _ => throw new InvalidOperationException("original")));

        var run = await orchestrator.RunAsync(42, "schedule", CancellationToken.None);

        Assert.Equal("ingest", run.FailedStep);
        Assert.Equal("InvalidOperationException: original", run.ErrorMessage);
    }

    private PipelineOrchestrator Create(FakeRunStore store, params IPipelineStep[] steps) =>
        new(steps, store, _clock);

    private FakeStep Step(string name, Action<RunContext>? action = null) => new(name, _log, action);
}
