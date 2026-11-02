using FeedbackIntel.Core.Configuration;
using FeedbackIntel.Core.Monitoring;
using FeedbackIntel.Core.Orchestration;
using FeedbackIntel.Core.Tests.Fakes;

namespace FeedbackIntel.Core.Tests.Monitoring;

public class FreshnessCheckStepTests
{
    private static readonly DateTime Now = new(2026, 11, 1, 13, 20, 0, DateTimeKind.Utc);

    private readonly FakeReporter _reporter = new();

    [Fact]
    public async Task ExecuteAsync_NoSuccessfulRunYet_ReportsNothing()
    {
        await RunAsync(lastSuccess: null);

        Assert.Empty(_reporter.Reported);
    }

    [Fact]
    public async Task ExecuteAsync_RecentSuccess_ReportsNothing()
    {
        await RunAsync(lastSuccess: Now.AddHours(-24));

        Assert.Empty(_reporter.Reported);
    }

    [Fact]
    public async Task ExecuteAsync_ExactlyAtThreshold_ReportsNothing()
    {
        await RunAsync(lastSuccess: Now.AddHours(-48));

        Assert.Empty(_reporter.Reported);
    }

    [Fact]
    public async Task ExecuteAsync_OlderThanThreshold_ReportsAge()
    {
        await RunAsync(lastSuccess: Now.AddHours(-49));

        Assert.Equal(TimeSpan.FromHours(49), Assert.Single(_reporter.Reported));
    }

    private Task RunAsync(DateTime? lastSuccess)
    {
        var step = new FreshnessCheckStep(
            new FakeRunStore { LastSuccessfulFinish = lastSuccess },
            _reporter,
            new OperationsOptions { StaleDataAlertHours = 48 },
            new FakeClock(Now));

        return step.ExecuteAsync(
            new RunContext(new RunRecord { RunId = 1, Trigger = "schedule", StartedAt = Now }),
            CancellationToken.None);
    }

    private sealed class FakeReporter : IStaleDataReporter
    {
        public List<TimeSpan> Reported { get; } = [];

        public void ReportStale(TimeSpan dataAge) => Reported.Add(dataAge);
    }
}
