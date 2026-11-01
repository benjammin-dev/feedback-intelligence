using FeedbackIntel.Core.Configuration;
using FeedbackIntel.Core.Ingestion;
using FeedbackIntel.Core.Model;
using FeedbackIntel.Core.Orchestration;
using FeedbackIntel.Core.Tests.Fakes;

namespace FeedbackIntel.Core.Tests.Ingestion;

public class IngestIssuesStepTests
{
    private static readonly DateTime Now = new(2026, 10, 24, 6, 0, 0, DateTimeKind.Utc);

    private readonly FakeIssueSource _source = new();
    private readonly FakeIssueStore _store = new();
    private readonly IngestionOptions _options = new() { InitialHistoryDays = 90, MaxRejectedPercent = 10 };

    [Fact]
    public async Task ExecuteAsync_FetchesEnabledRepositoriesFromHistoryWindow()
    {
        var repositories = new List<RepositoryOptions>
        {
            new() { Owner = "microsoft", Name = "winget-cli", Enabled = true },
            new() { Owner = "microsoft", Name = "PowerToys", Enabled = false },
        };

        await RunAsync(repositories);

        var call = Assert.Single(_source.Calls);
        Assert.Equal("microsoft/winget-cli", call.Repository.FullName);
        Assert.Equal(Now.AddDays(-90), call.Since);
    }

    [Fact]
    public async Task ExecuteAsync_ClearsRawTableBeforeInserting()
    {
        _source.Issues = [TestIssues.Valid(1)];

        await RunAsync();

        Assert.Equal<string>(["clear", "insert"], _store.Operations);
    }

    [Fact]
    public async Task ExecuteAsync_StoresValidIssuesAndCountsRejected()
    {
        _source.Issues = [.. Enumerable.Range(1, 19).Select(n => TestIssues.Valid(n)), TestIssues.Valid(20) with { Title = "" }];

        var run = await RunAsync();

        Assert.Equal(19, _store.Inserted.Count);
        Assert.DoesNotContain(_store.Inserted, issue => issue.IssueNumber == 20);
        Assert.Equal(20, run.IssuesFetched);
        Assert.Equal(1, run.IssuesRejected);
    }

    [Fact]
    public async Task ExecuteAsync_RejectedExactlyAtLimit_Succeeds()
    {
        _source.Issues = [.. Enumerable.Range(1, 9).Select(n => TestIssues.Valid(n)), TestIssues.Valid(10) with { Title = "" }];

        var run = await RunAsync();

        Assert.Equal(1, run.IssuesRejected);
    }

    [Fact]
    public async Task ExecuteAsync_RejectedAboveLimit_Throws()
    {
        _source.Issues = [.. Enumerable.Range(1, 8).Select(n => TestIssues.Valid(n)), .. Enumerable.Range(9, 2).Select(n => TestIssues.Valid(n) with { Title = "" })];

        var error = await Assert.ThrowsAsync<InvalidDataException>(() => RunAsync());

        Assert.Contains("2 of 10", error.Message);
    }

    [Fact]
    public async Task ExecuteAsync_DuplicateIssue_KeepsLatestCopy()
    {
        var older = TestIssues.Valid(5);
        var newer = older with { UpdatedAt = older.UpdatedAt.AddHours(1), Title = "updated title" };
        _source.Issues = [older, newer];

        var run = await RunAsync();

        var stored = Assert.Single(_store.Inserted);
        Assert.Equal("updated title", stored.Title);
        Assert.Equal(1, run.IssuesFetched);
    }

    [Fact]
    public async Task ExecuteAsync_NoIssues_Succeeds()
    {
        var run = await RunAsync();

        Assert.Equal(0, run.IssuesFetched);
        Assert.Empty(_store.Inserted);
    }

    private async Task<RunRecord> RunAsync(List<RepositoryOptions>? repositories = null)
    {
        repositories ??= [new() { Owner = "microsoft", Name = "winget-cli" }];
        var step = new IngestIssuesStep(repositories, _options, _source, _store, new FakeClock(Now));
        var context = new RunContext(new RunRecord { RunId = 1, Trigger = "manual", StartedAt = Now });

        await step.ExecuteAsync(context, CancellationToken.None);
        return context.Run;
    }

    private sealed class FakeIssueSource : IIssueSource
    {
        public List<RawIssue> Issues { get; set; } = [];

        public List<(TrackedRepository Repository, DateTime Since)> Calls { get; } = [];

        public Task<IReadOnlyList<RawIssue>> FetchUpdatedSinceAsync(
            TrackedRepository repository, DateTime since, CancellationToken cancellationToken)
        {
            Calls.Add((repository, since));
            return Task.FromResult<IReadOnlyList<RawIssue>>(Issues);
        }
    }

    private sealed class FakeIssueStore : IIssueStore
    {
        public List<string> Operations { get; } = [];

        public List<RawIssue> Inserted { get; } = [];

        public Task<TrackedRepository> EnsureRepositoryAsync(
            string owner, string name, DateTime onboardedAt, CancellationToken cancellationToken) =>
            Task.FromResult(new TrackedRepository(1, owner, name));

        public Task ClearRawIssuesAsync(CancellationToken cancellationToken)
        {
            Operations.Add("clear");
            return Task.CompletedTask;
        }

        public Task InsertRawIssuesAsync(IReadOnlyCollection<RawIssue> issues, CancellationToken cancellationToken)
        {
            Operations.Add("insert");
            Inserted.AddRange(issues);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<RawIssue>> ReadRawIssuesAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<RawIssue>>(Inserted);
    }
}
