using FeedbackIntel.Core.Orchestration;
using FeedbackIntel.Core.Quality;
using FeedbackIntel.Core.Tests.Fakes;

namespace FeedbackIntel.Core.Tests.Quality;

public class QualityChecksStepTests
{
    private static readonly DateTime Now = new(2026, 11, 1, 13, 20, 0, DateTimeKind.Utc);

    private static RunContext Context() =>
        new(new RunRecord { RunId = 77, Trigger = "schedule", StartedAt = Now });

    [Fact]
    public async Task ExecuteAsync_NoProblems_Passes()
    {
        var checker = new FakeChecker([]);

        await new QualityChecksStep(checker, new FakeClock(Now)).ExecuteAsync(Context(), CancellationToken.None);

        Assert.Equal((77L, Now), Assert.Single(checker.Calls));
    }

    [Fact]
    public async Task ExecuteAsync_Problems_ThrowsListingEveryOne()
    {
        var checker = new FakeChecker(["First check failed (2 rows).", "Second check failed (1 rows)."]);

        var error = await Assert.ThrowsAsync<InvalidDataException>(
            () => new QualityChecksStep(checker, new FakeClock(Now)).ExecuteAsync(Context(), CancellationToken.None));

        Assert.Equal(
            "2 quality check(s) failed: First check failed (2 rows). Second check failed (1 rows).",
            error.Message);
    }

    private sealed class FakeChecker(IReadOnlyList<string> problems) : IQualityChecker
    {
        public List<(long RunId, DateTime Now)> Calls { get; } = [];

        public Task<IReadOnlyList<string>> FindProblemsAsync(long runId, DateTime now, CancellationToken cancellationToken)
        {
            Calls.Add((runId, now));
            return Task.FromResult(problems);
        }
    }
}
