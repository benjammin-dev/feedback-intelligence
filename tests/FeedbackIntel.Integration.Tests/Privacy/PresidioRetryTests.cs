using System.Net;

using FeedbackIntel.Infrastructure.Privacy;

namespace FeedbackIntel.Integration.Tests.Privacy;

/// <summary>Which Presidio errors are retried. Redaction failures that remain still stop the run.</summary>
public class PresidioRetryTests
{
    [Fact]
    public void RetryAfter_NoConnection_BacksOff()
    {
        Assert.Equal(TimeSpan.FromSeconds(2), PresidioRedactor.RetryAfter(new HttpRequestException("refused"), retry: 1));
    }

    [Theory]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    public void RetryAfter_ServerError_BacksOff(HttpStatusCode status)
    {
        var error = new HttpRequestException("server error", null, status);

        Assert.Equal(TimeSpan.FromSeconds(8), PresidioRedactor.RetryAfter(error, retry: 3));
    }

    [Fact]
    public void RetryAfter_BadRequest_IsNotRetried()
    {
        var error = new HttpRequestException("bad request", null, HttpStatusCode.BadRequest);

        Assert.Null(PresidioRedactor.RetryAfter(error, retry: 1));
    }

    [Fact]
    public void RetryAfter_Timeout_BacksOff()
    {
        Assert.Equal(TimeSpan.FromSeconds(2), PresidioRedactor.RetryAfter(new TaskCanceledException(), retry: 1));
    }
}
