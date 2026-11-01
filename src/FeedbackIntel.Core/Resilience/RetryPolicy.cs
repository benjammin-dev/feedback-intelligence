namespace FeedbackIntel.Core.Resilience;

/// <summary>
/// Retries temporary failures (design, error-handling rule 1): up to 3 retries, waiting
/// 2, 4, then 8 seconds. Each caller supplies a classifier that decides whether an error is
/// temporary, and can ask for a specific wait instead, such as until a rate limit resets.
/// Permanent errors (bad data, bad configuration, bugs) are never retried.
/// </summary>
/// <param name="delay">How to wait. Tests pass a fake so they don't really sleep.</param>
public sealed class RetryPolicy(Func<TimeSpan, CancellationToken, Task> delay)
{
    /// <summary>How many times a failed call is tried again.</summary>
    public const int MaxRetries = 3;

    /// <summary>The policy used in production, which really waits.</summary>
    public static RetryPolicy Default { get; } = new(Task.Delay);

    /// <summary>The standard wait before retry number <paramref name="retry"/>: 2, 4, then 8 seconds.</summary>
    public static TimeSpan Backoff(int retry) => TimeSpan.FromSeconds(Math.Pow(2, retry));

    /// <summary>
    /// Runs <paramref name="action"/>, retrying while <paramref name="retryAfter"/> returns a
    /// wait for the error. When it returns null, or retries run out, the error is rethrown.
    /// </summary>
    /// <param name="action">The call to make.</param>
    /// <param name="retryAfter">
    /// Given the error and the retry number (1 to 3), returns how long to wait before trying
    /// again, or null if the error is permanent.
    /// </param>
    /// <param name="cancellationToken">Stops waiting and retrying when the run is cancelled.</param>
    public async Task<T> ExecuteAsync<T>(
        Func<CancellationToken, Task<T>> action,
        Func<Exception, int, TimeSpan?> retryAfter,
        CancellationToken cancellationToken)
    {
        for (var retry = 1; ; retry++)
        {
            try
            {
                return await action(cancellationToken);
            }
            catch (Exception ex) when (retry <= MaxRetries
                && !cancellationToken.IsCancellationRequested
                && retryAfter(ex, retry) is { } wait)
            {
                await delay(wait, cancellationToken);
            }
        }
    }
}
