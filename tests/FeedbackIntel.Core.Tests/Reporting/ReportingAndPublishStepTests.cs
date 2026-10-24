using FeedbackIntel.Core.Orchestration;
using FeedbackIntel.Core.Publishing;
using FeedbackIntel.Core.Reporting;

namespace FeedbackIntel.Core.Tests.Reporting;

public class ReportingAndPublishStepTests
{
    private static readonly DateTime Start = new(2026, 10, 24, 6, 0, 0, DateTimeKind.Utc);

    private static RunContext Context() =>
        new(new RunRecord { RunId = 99, Trigger = "schedule", StartedAt = Start });

    [Fact]
    public async Task RebuildReportingStep_UsesRunStartTimeAndId()
    {
        var builder = new FakeBuilder();

        await new RebuildReportingStep(builder).ExecuteAsync(Context(), CancellationToken.None);

        Assert.Equal((Start, 99L), Assert.Single(builder.Calls));
    }

    [Fact]
    public async Task PublishStep_CallsPublisherOnce()
    {
        var publisher = new FakePublisher();

        await new PublishStep(publisher).ExecuteAsync(Context(), CancellationToken.None);

        Assert.Equal(1, publisher.Calls);
    }

    private sealed class FakeBuilder : IReportingBuilder
    {
        public List<(DateTime AsOf, long RunId)> Calls { get; } = [];

        public Task RebuildAsync(DateTime asOf, long runId, CancellationToken cancellationToken)
        {
            Calls.Add((asOf, runId));
            return Task.CompletedTask;
        }
    }

    private sealed class FakePublisher : IPublisher
    {
        public int Calls { get; private set; }

        public Task PublishAsync(CancellationToken cancellationToken)
        {
            Calls++;
            return Task.CompletedTask;
        }
    }
}
