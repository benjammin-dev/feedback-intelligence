using FeedbackIntel.Core.Configuration;
using FeedbackIntel.Core.Ingestion;
using FeedbackIntel.Core.Model;
using FeedbackIntel.Core.Orchestration;
using FeedbackIntel.Core.Privacy;
using FeedbackIntel.Core.Tests.Ingestion;
using FeedbackIntel.Core.Text;

namespace FeedbackIntel.Core.Tests.Privacy;

public class RedactIssuesStepTests
{
    private readonly FakeRawStore _raw = new();
    private readonly FakeCleanedStore _cleaned = new();

    [Fact]
    public async Task ExecuteAsync_RedactsTitleAndBody()
    {
        _raw.Issues = [TestIssues.Valid() with { Title = "Crash SECRET", Body = "log SECRET and SECRET" }];

        var issue = Assert.Single(await RunAsync());

        Assert.Equal("Crash <X>", issue.TitleRedacted);
        Assert.Equal("log <X> and <X>", issue.BodyRedacted);
        Assert.Equal(3, issue.RedactionCount);
    }

    [Fact]
    public async Task ExecuteAsync_HashesRedactedText()
    {
        _raw.Issues = [TestIssues.Valid() with { Title = "Crash SECRET", Body = "log SECRET" }];

        var issue = Assert.Single(await RunAsync());

        Assert.Equal(ContentHasher.Compute("Crash <X>", "log <X>"), issue.ContentHash);
    }

    [Fact]
    public async Task ExecuteAsync_LongBody_IsTruncatedAfterHashing()
    {
        var sharedStart = new string('a', 20);
        _raw.Issues =
        [
            TestIssues.Valid(1) with { Body = sharedStart + "first ending" },
            TestIssues.Valid(2) with { Body = sharedStart + "second ending" },
        ];

        var issues = (await RunAsync(bodyMaxCharacters: 20)).OrderBy(issue => issue.IssueNumber).ToList();

        Assert.All(issues, issue => Assert.Equal(sharedStart, issue.BodyRedacted));
        Assert.All(issues, issue => Assert.True(issue.BodyTruncated));
        Assert.NotEqual(issues[0].ContentHash, issues[1].ContentHash);
        Assert.Equal(32, issues[0].BodyLength);
    }

    [Fact]
    public async Task ExecuteAsync_ShortBody_IsNotTruncated()
    {
        _raw.Issues = [TestIssues.Valid() with { Body = "short" }];

        var issue = Assert.Single(await RunAsync());

        Assert.False(issue.BodyTruncated);
        Assert.Equal(5, issue.BodyLength);
    }

    [Fact]
    public async Task ExecuteAsync_NullBody_StaysEmpty()
    {
        _raw.Issues = [TestIssues.Valid() with { Body = null }];

        var issue = Assert.Single(await RunAsync());

        Assert.Null(issue.BodyRedacted);
        Assert.Equal(0, issue.BodyLength);
        Assert.False(issue.BodyTruncated);
    }

    [Fact]
    public async Task ExecuteAsync_CopiesIssueDetails()
    {
        var raw = TestIssues.Valid(7) with
        {
            State = "closed",
            StateReason = "completed",
            ClosedAt = TestIssues.Created.AddDays(2),
        };
        _raw.Issues = [raw];

        var issue = Assert.Single(await RunAsync());

        Assert.Equal(raw.RepoId, issue.RepoId);
        Assert.Equal(7, issue.IssueNumber);
        Assert.Equal("closed", issue.State);
        Assert.Equal("completed", issue.StateReason);
        Assert.Equal(raw.LabelsJson, issue.LabelsJson);
        Assert.Equal(raw.CreatedAt, issue.CreatedAt);
        Assert.Equal(raw.UpdatedAt, issue.UpdatedAt);
        Assert.Equal(raw.ClosedAt, issue.ClosedAt);
        Assert.Equal(raw.HtmlUrl, issue.HtmlUrl);
    }

    [Fact]
    public async Task ExecuteAsync_RecordsRunAndCount()
    {
        _raw.Issues = [TestIssues.Valid(1), TestIssues.Valid(2), TestIssues.Valid(3)];

        var context = await RunWithContextAsync();

        Assert.Equal(3, context.Run.IssuesRedacted);
        Assert.Equal(context.Run.RunId, _cleaned.RunId);
    }

    [Fact]
    public async Task ExecuteAsync_RedactionFails_StopsWithoutStoring()
    {
        _raw.Issues = [TestIssues.Valid()];
        var step = new RedactIssuesStep(_raw, new FailingRedactor(), _cleaned, new TextOptions());

        await Assert.ThrowsAsync<HttpRequestException>(() => step.ExecuteAsync(NewContext(), CancellationToken.None));

        Assert.Null(_cleaned.Stored);
    }

    private async Task<IReadOnlyCollection<CleanedIssue>> RunAsync(int bodyMaxCharacters = 4000)
    {
        await RunWithContextAsync(bodyMaxCharacters);
        return _cleaned.Stored!;
    }

    private async Task<RunContext> RunWithContextAsync(int bodyMaxCharacters = 4000)
    {
        var step = new RedactIssuesStep(
            _raw, new FakeRedactor(), _cleaned, new TextOptions { BodyMaxCharacters = bodyMaxCharacters });
        var context = NewContext();
        await step.ExecuteAsync(context, CancellationToken.None);
        return context;
    }

    private static RunContext NewContext() =>
        new(new RunRecord { RunId = 555, Trigger = "manual", StartedAt = TestIssues.Created });

    /// <summary>Treats the word SECRET as personal information.</summary>
    private sealed class FakeRedactor : IPiiRedactor
    {
        public Task<RedactedText> RedactAsync(string text, CancellationToken cancellationToken)
        {
            var count = text.Split("SECRET").Length - 1;
            return Task.FromResult(new RedactedText(text.Replace("SECRET", "<X>", StringComparison.Ordinal), count));
        }
    }

    private sealed class FailingRedactor : IPiiRedactor
    {
        public Task<RedactedText> RedactAsync(string text, CancellationToken cancellationToken) =>
            throw new HttpRequestException("Presidio unavailable");
    }

    private sealed class FakeRawStore : IIssueStore
    {
        public List<RawIssue> Issues { get; set; } = [];

        public Task<IReadOnlyList<RawIssue>> ReadRawIssuesAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<RawIssue>>(Issues);

        public Task<TrackedRepository> EnsureRepositoryAsync(
            string owner, string name, DateTime onboardedAt, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task ClearRawIssuesAsync(CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task InsertRawIssuesAsync(IReadOnlyCollection<RawIssue> issues, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private sealed class FakeCleanedStore : ICleanedIssueStore
    {
        public IReadOnlyCollection<CleanedIssue>? Stored { get; private set; }

        public long RunId { get; private set; }

        public Task UpsertAsync(IReadOnlyCollection<CleanedIssue> issues, long runId, CancellationToken cancellationToken)
        {
            Stored = issues;
            RunId = runId;
            return Task.CompletedTask;
        }
    }
}
