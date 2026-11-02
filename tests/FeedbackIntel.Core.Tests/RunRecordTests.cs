using FeedbackIntel.Core.Orchestration;

namespace FeedbackIntel.Core.Tests;

public class RunRecordTests
{
    private static readonly DateTime Start = new(2026, 11, 1, 13, 17, 0, DateTimeKind.Utc);

    [Fact]
    public void AsSucceeded_KeepsBackfillRangeAndCounters()
    {
        var run = new RunRecord
        {
            RunId = 5,
            Trigger = "manual",
            StartedAt = Start,
            BackfillFrom = new DateTime(2026, 8, 1, 0, 0, 0, DateTimeKind.Utc),
            BackfillTo = new DateTime(2026, 8, 31, 0, 0, 0, DateTimeKind.Utc),
            IssuesFetched = 40,
        };

        var succeeded = run.AsSucceeded(Start.AddMinutes(9));

        Assert.Equal(RunStatus.Succeeded, succeeded.Status);
        Assert.Equal(run.BackfillFrom, succeeded.BackfillFrom);
        Assert.Equal(run.BackfillTo, succeeded.BackfillTo);
        Assert.Equal(40, succeeded.IssuesFetched);
        Assert.Equal(Start.AddMinutes(9), succeeded.FinishedAt);
    }
}
