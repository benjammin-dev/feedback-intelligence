using FeedbackIntel.Core.Orchestration;

namespace FeedbackIntel.Core.Tests.Fakes;

/// <summary>Keeps saved run records in memory, or fails on request.</summary>
internal sealed class FakeRunStore : IRunStore
{
    public List<RunRecord> Saved { get; } = [];

    public bool ShouldFail { get; init; }

    public Task SaveAsync(RunRecord run, CancellationToken cancellationToken)
    {
        if (ShouldFail)
        {
            throw new InvalidOperationException("Database unavailable");
        }

        Saved.Add(run);
        return Task.CompletedTask;
    }
}
