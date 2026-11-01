using System.Net;

using FeedbackIntel.Infrastructure.GitHub;

using Octokit;

namespace FeedbackIntel.Integration.Tests.GitHub;

/// <summary>Which GitHub errors are retried, and how long the pipeline waits (FR-ING-04).</summary>
public class GitHubRetryTests
{
    private static readonly TimeSpan MaxWait = TimeSpan.FromMinutes(15);

    [Theory]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.BadGateway)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    public void RetryAfter_ServerError_BacksOff(HttpStatusCode status)
    {
        var wait = GitHubRetry.RetryAfter(new ApiException("server error", status), retry: 2, MaxWait);

        Assert.Equal(TimeSpan.FromSeconds(4), wait);
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.UnprocessableEntity)]
    public void RetryAfter_ClientError_IsNotRetried(HttpStatusCode status)
    {
        Assert.Null(GitHubRetry.RetryAfter(new ApiException("client error", status), retry: 1, MaxWait));
    }

    [Fact]
    public void RetryAfter_NetworkError_BacksOff()
    {
        Assert.Equal(TimeSpan.FromSeconds(2), GitHubRetry.RetryAfter(new HttpRequestException("reset"), retry: 1, MaxWait));
    }

    [Fact]
    public void RetryAfter_Timeout_BacksOff()
    {
        Assert.Equal(TimeSpan.FromSeconds(2), GitHubRetry.RetryAfter(new TaskCanceledException(), retry: 1, MaxWait));
    }

    [Fact]
    public void RetryAfter_UnrelatedError_IsNotRetried()
    {
        Assert.Null(GitHubRetry.RetryAfter(new InvalidOperationException("bug"), retry: 1, MaxWait));
    }

    [Fact]
    public void RateLimitWait_ResetsSoon_WaitsUntilResetPlusMargin()
    {
        var wait = GitHubRetry.RateLimitWait(TimeSpan.FromMinutes(10), MaxWait);

        Assert.Equal(TimeSpan.FromMinutes(10) + GitHubRetry.ResetMargin, wait);
    }

    [Fact]
    public void RateLimitWait_ResetsTooLate_GivesUp()
    {
        Assert.Null(GitHubRetry.RateLimitWait(TimeSpan.FromMinutes(40), MaxWait));
    }

    [Fact]
    public void RateLimitWait_AlreadyReset_WaitsOnlyTheMargin()
    {
        Assert.Equal(GitHubRetry.ResetMargin, GitHubRetry.RateLimitWait(TimeSpan.FromSeconds(-30), MaxWait));
    }
}
