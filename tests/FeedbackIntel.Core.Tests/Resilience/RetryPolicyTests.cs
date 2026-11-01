using FeedbackIntel.Core.Resilience;

namespace FeedbackIntel.Core.Tests.Resilience;

public class RetryPolicyTests
{
    private readonly List<TimeSpan> _waits = [];
    private int _attempts;

    private RetryPolicy Policy => new((wait, _) =>
    {
        _waits.Add(wait);
        return Task.CompletedTask;
    });

    /// <summary>Treats TimeoutException as temporary and everything else as permanent.</summary>
    private static TimeSpan? Classify(Exception error, int retry) =>
        error is TimeoutException ? RetryPolicy.Backoff(retry) : null;

    [Fact]
    public async Task ExecuteAsync_Succeeds_DoesNotWait()
    {
        var result = await Policy.ExecuteAsync(_ => Attempt(failuresBeforeSuccess: 0), Classify, CancellationToken.None);

        Assert.Equal("ok", result);
        Assert.Empty(_waits);
    }

    [Fact]
    public async Task ExecuteAsync_TemporaryFailures_RetriesWithIncreasingWaits()
    {
        var result = await Policy.ExecuteAsync(_ => Attempt(failuresBeforeSuccess: 2), Classify, CancellationToken.None);

        Assert.Equal("ok", result);
        Assert.Equal<TimeSpan>([TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(4)], _waits);
    }

    [Fact]
    public async Task ExecuteAsync_KeepsFailing_GivesUpAfterThreeRetries()
    {
        await Assert.ThrowsAsync<TimeoutException>(
            () => Policy.ExecuteAsync(_ => Attempt(failuresBeforeSuccess: 10), Classify, CancellationToken.None));

        Assert.Equal(4, _attempts);
        Assert.Equal<TimeSpan>([TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(4), TimeSpan.FromSeconds(8)], _waits);
    }

    [Fact]
    public async Task ExecuteAsync_PermanentFailure_IsNotRetried()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() => Policy.ExecuteAsync<string>(
            _ =>
            {
                _attempts++;
                throw new InvalidOperationException("bad data");
            },
            Classify,
            CancellationToken.None));

        Assert.Equal(1, _attempts);
        Assert.Empty(_waits);
    }

    [Fact]
    public async Task ExecuteAsync_ClassifierChoosesWait_UsesIt()
    {
        var rateLimitWait = TimeSpan.FromMinutes(3);

        await Policy.ExecuteAsync(_ => Attempt(failuresBeforeSuccess: 1), (_, _) => rateLimitWait, CancellationToken.None);

        Assert.Equal(rateLimitWait, Assert.Single(_waits));
    }

    [Fact]
    public async Task ExecuteAsync_Cancelled_DoesNotRetry()
    {
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        await Assert.ThrowsAsync<TimeoutException>(
            () => Policy.ExecuteAsync(_ => Attempt(failuresBeforeSuccess: 1), Classify, cancellation.Token));

        Assert.Equal(1, _attempts);
    }

    [Theory]
    [InlineData(1, 2)]
    [InlineData(2, 4)]
    [InlineData(3, 8)]
    public void Backoff_DoublesEachRetry(int retry, int expectedSeconds)
    {
        Assert.Equal(TimeSpan.FromSeconds(expectedSeconds), RetryPolicy.Backoff(retry));
    }

    private Task<string> Attempt(int failuresBeforeSuccess)
    {
        _attempts++;
        return _attempts <= failuresBeforeSuccess
            ? throw new TimeoutException("temporary")
            : Task.FromResult("ok");
    }
}
