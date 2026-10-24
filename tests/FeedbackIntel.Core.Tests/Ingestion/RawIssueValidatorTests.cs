using FeedbackIntel.Core.Ingestion;

namespace FeedbackIntel.Core.Tests.Ingestion;

public class RawIssueValidatorTests
{
    [Fact]
    public void FindProblem_ValidIssue_ReturnsNull()
    {
        Assert.Null(RawIssueValidator.FindProblem(TestIssues.Valid()));
    }

    [Fact]
    public void FindProblem_ClosedIssueWithDate_ReturnsNull()
    {
        var issue = TestIssues.Valid() with
        {
            State = "closed",
            StateReason = "completed",
            ClosedAt = TestIssues.Created.AddDays(3),
        };

        Assert.Null(RawIssueValidator.FindProblem(issue));
    }

    [Fact]
    public void FindProblem_NullBody_IsAllowed()
    {
        Assert.Null(RawIssueValidator.FindProblem(TestIssues.Valid() with { Body = null }));
    }

    [Fact]
    public void FindProblem_NonPositiveNumber_IsRejected()
    {
        Assert.NotNull(RawIssueValidator.FindProblem(TestIssues.Valid() with { IssueNumber = 0 }));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void FindProblem_EmptyTitle_IsRejected(string title)
    {
        Assert.NotNull(RawIssueValidator.FindProblem(TestIssues.Valid() with { Title = title }));
    }

    [Fact]
    public void FindProblem_TitleTooLong_IsRejected()
    {
        var issue = TestIssues.Valid() with { Title = new string('a', RawIssueValidator.MaxTitleLength + 1) };

        Assert.NotNull(RawIssueValidator.FindProblem(issue));
    }

    [Fact]
    public void FindProblem_UnknownState_IsRejected()
    {
        Assert.NotNull(RawIssueValidator.FindProblem(TestIssues.Valid() with { State = "merged" }));
    }

    [Fact]
    public void FindProblem_ClosedWithoutDate_IsRejected()
    {
        Assert.NotNull(RawIssueValidator.FindProblem(TestIssues.Valid() with { State = "closed" }));
    }

    [Fact]
    public void FindProblem_UpdatedBeforeCreated_IsRejected()
    {
        var issue = TestIssues.Valid() with { UpdatedAt = TestIssues.Created.AddDays(-1) };

        Assert.NotNull(RawIssueValidator.FindProblem(issue));
    }

    [Fact]
    public void FindProblem_LabelsTooLong_IsRejected()
    {
        var labels = Enumerable.Range(0, 100).Select(i => $"label-with-a-long-name-{i}").ToList();

        Assert.NotNull(RawIssueValidator.FindProblem(TestIssues.Valid() with { Labels = labels }));
    }

    [Theory]
    [InlineData("")]
    [InlineData("http://github.com/microsoft/winget-cli/issues/1")]
    [InlineData("https://example.com/issues/1")]
    public void FindProblem_BadUrl_IsRejected(string url)
    {
        Assert.NotNull(RawIssueValidator.FindProblem(TestIssues.Valid() with { HtmlUrl = url }));
    }

    [Fact]
    public void FindProblem_Reason_NeverContainsIssueText()
    {
        var issue = TestIssues.Valid() with { State = "secret-state", Title = "secret-title" };

        var problem = RawIssueValidator.FindProblem(issue);

        Assert.DoesNotContain("secret", problem);
    }
}
