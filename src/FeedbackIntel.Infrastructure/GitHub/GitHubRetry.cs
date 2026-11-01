using FeedbackIntel.Core.Resilience;

using Octokit;

namespace FeedbackIntel.Infrastructure.GitHub;

/// <summary>
/// Decides which GitHub API errors are worth retrying, and for how long to wait (FR-ING-04).
/// </summary>
internal static class GitHubRetry
{
    /// <summary>GitHub asks clients to wait at least a minute after a secondary rate limit.</summary>
    internal static readonly TimeSpan SecondaryRateLimitWait = TimeSpan.FromMinutes(1);

    /// <summary>A little extra after a rate limit resets, in case the clocks differ slightly.</summary>
    internal static readonly TimeSpan ResetMargin = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Returns how long to wait before retrying, or null if the error is permanent or the
    /// rate limit resets too far in the future to wait for.
    /// </summary>
    internal static TimeSpan? RetryAfter(Exception error, int retry, TimeSpan maxRateLimitWait) => error switch
    {
        RateLimitExceededException limit => RateLimitWait(limit.GetRetryAfterTimeSpan(), maxRateLimitWait),
        SecondaryRateLimitExceededException or AbuseException => SecondaryRateLimitWait,
        ApiException api when (int)api.StatusCode >= 500 => RetryPolicy.Backoff(retry),
        ApiException => null,
        HttpRequestException => RetryPolicy.Backoff(retry),
        TaskCanceledException => RetryPolicy.Backoff(retry),
        _ => null,
    };

    /// <summary>
    /// Waits for the rate limit to reset only if that happens within the configured limit;
    /// otherwise the run fails and the next scheduled run picks up from the watermark.
    /// </summary>
    internal static TimeSpan? RateLimitWait(TimeSpan untilReset, TimeSpan maxWait)
    {
        var wait = (untilReset > TimeSpan.Zero ? untilReset : TimeSpan.Zero) + ResetMargin;
        return wait <= maxWait ? wait : null;
    }
}
