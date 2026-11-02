using FeedbackIntel.Core.Configuration;

namespace FeedbackIntel.Core.Tests.Configuration;

public class BackfillOptionsTests
{
    private static readonly DateOnly Today = new(2026, 11, 1);

    [Theory]
    [InlineData("", "")]
    [InlineData("2026-08-01", "")]
    [InlineData("2026-08-01", "2026-08-01")]
    [InlineData(" 2026-08-01 ", "2026-08-31")]
    public void FindProblem_ValidOrEmpty_ReturnsNull(string from, string to)
    {
        Assert.Null(new BackfillOptions { From = from, To = to }.FindProblem());
    }

    [Theory]
    [InlineData("", "2026-08-31", "needs a Backfill:From")]
    [InlineData("08/01/2026", "", "must be dates")]
    [InlineData("2026-08-01", "2026-13-01", "must be dates")]
    [InlineData("2026-08-31", "2026-08-01", "must not be before")]
    public void FindProblem_Invalid_IsReported(string from, string to, string expected)
    {
        Assert.Contains(expected, new BackfillOptions { From = from, To = to }.FindProblem());
    }

    [Fact]
    public void ToRange_NotRequested_ReturnsNull()
    {
        Assert.Null(new BackfillOptions().ToRange(Today));
    }

    [Fact]
    public void ToRange_NoEndDate_EndsToday()
    {
        var range = new BackfillOptions { From = "2026-08-01" }.ToRange(Today);

        Assert.Equal(new BackfillRange(new DateOnly(2026, 8, 1), Today), range);
    }

    [Fact]
    public void Range_IncludesWholeLastDay()
    {
        var range = new BackfillRange(new DateOnly(2026, 8, 1), new DateOnly(2026, 8, 31));

        Assert.False(range.Contains(new DateTime(2026, 7, 31, 23, 59, 59, DateTimeKind.Utc)));
        Assert.True(range.Contains(new DateTime(2026, 8, 1, 0, 0, 0, DateTimeKind.Utc)));
        Assert.True(range.Contains(new DateTime(2026, 8, 31, 23, 59, 59, DateTimeKind.Utc)));
        Assert.False(range.Contains(new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc)));
    }
}
