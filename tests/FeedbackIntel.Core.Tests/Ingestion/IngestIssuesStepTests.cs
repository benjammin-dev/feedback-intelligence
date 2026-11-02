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
    private readonly FakeWatermarkStore _watermarks = new();
    private readonly IngestionOptions _options = new()
    {
        InitialHistoryDays = 90,
        MaxRejectedPercent = 10,
        MinIssuesForRejectionCheck = 20,
    };

    private BackfillOptions _backfill = new();

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
        _source.Issues = [.. Valid(1, 18), .. Invalid(19, 2)];

        var run = await RunAsync();

        Assert.Equal(2, run.IssuesRejected);
    }

    [Fact]
    public async Task ExecuteAsync_RejectedAboveLimit_Throws()
    {
        _source.Issues = [.. Valid(1, 17), .. Invalid(18, 3)];

        var error = await Assert.ThrowsAsync<InvalidDataException>(() => RunAsync());

        Assert.Contains("3 of 20", error.Message);
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

    [Fact]
    public async Task ExecuteAsync_SmallRunWithRejections_IsNotJudgedByPercentage()
    {
        _source.Issues = [.. Valid(1, 4), .. Invalid(5, 1)];

        var run = await RunAsync();

        Assert.Equal(5, run.IssuesFetched);
        Assert.Equal(1, run.IssuesRejected);
    }

    [Fact]
    public async Task ExecuteAsync_RepositoryWithWatermark_FetchesSinceWatermark()
    {
        var mark = Now.AddHours(-26);
        _watermarks.Marks[1] = mark;

        await RunAsync();

        Assert.Equal(mark, Assert.Single(_source.Calls).Since);
    }

    [Fact]
    public async Task ExecuteAsync_WatermarkForOtherRepository_UsesHistoryWindow()
    {
        _watermarks.Marks[2] = Now.AddHours(-26);

        await RunAsync();

        Assert.Equal(Now.AddDays(-90), Assert.Single(_source.Calls).Since);
    }

    [Fact]
    public async Task ExecuteAsync_NeverMovesWatermarks()
    {
        _source.Issues = [.. Valid(1, 3)];

        await RunAsync();

        Assert.Equal(0, _watermarks.AdvanceCalls);
    }

    [Fact]
    public async Task ExecuteAsync_NoBackfill_LeavesRangeEmpty()
    {
        var run = await RunAsync();

        Assert.Null(run.BackfillFrom);
        Assert.Null(run.BackfillTo);
    }

    [Fact]
    public async Task ExecuteAsync_BackfillBeforeWatermark_FetchesFromRangeStartAndKeepsBothSets()
    {
        var mark = Now.AddHours(-26);
        _watermarks.Marks[1] = mark;
        _backfill = new BackfillOptions { From = "2026-08-01", To = "2026-08-31" };
        _source.Issues =
        [
            Issue(1, created: new(2026, 10, 1), updated: mark.AddHours(1)),      // updated since watermark: kept
            Issue(2, created: new(2026, 8, 1), updated: new(2026, 9, 1)),        // created on the first day: kept
            Issue(3, created: new(2026, 8, 31, 23, 0, 0), updated: new(2026, 9, 2)), // created on the last day: kept
            Issue(4, created: new(2026, 9, 1), updated: new(2026, 9, 3)),        // after the range, not updated since: dropped
            Issue(5, created: new(2026, 7, 31), updated: new(2026, 8, 2)),       // before the range, not updated since: dropped
        ];

        var run = await RunAsync();

        Assert.Equal(new DateTime(2026, 8, 1, 0, 0, 0, DateTimeKind.Utc), Assert.Single(_source.Calls).Since);
        Assert.Equal<int>([1, 2, 3], _store.Inserted.Select(issue => issue.IssueNumber).Order());
        Assert.Equal(3, run.IssuesFetched);
        Assert.Equal(new DateTime(2026, 8, 1, 0, 0, 0, DateTimeKind.Utc), run.BackfillFrom);
        Assert.Equal(new DateTime(2026, 8, 31, 0, 0, 0, DateTimeKind.Utc), run.BackfillTo);
    }

    [Fact]
    public async Task ExecuteAsync_BackfillAfterWatermark_FetchesFromWatermark()
    {
        var mark = Now.AddDays(-3);
        _watermarks.Marks[1] = mark;
        _backfill = new BackfillOptions { From = "2026-10-23" };

        var run = await RunAsync();

        Assert.Equal(mark, Assert.Single(_source.Calls).Since);
        Assert.Equal(new DateTime(2026, 10, 24, 0, 0, 0, DateTimeKind.Utc), run.BackfillTo);
    }

    [Fact]
    public async Task ExecuteAsync_BackfillTooFarBack_Throws()
    {
        _backfill = new BackfillOptions { From = "2025-10-23", To = "2025-12-31" };

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => RunAsync());

        Assert.Contains("at most 365 days ago", error.Message);
        Assert.Empty(_source.Calls);
    }

    [Fact]
    public async Task ExecuteAsync_BackfillExactlyAtLimit_IsAllowed()
    {
        _backfill = new BackfillOptions { From = "2025-10-24" };

        await RunAsync();

        Assert.Equal(new DateTime(2025, 10, 24, 0, 0, 0, DateTimeKind.Utc), Assert.Single(_source.Calls).Since);
    }

    private static RawIssue Issue(int number, DateTime created, DateTime updated) =>
        TestIssues.Valid(number) with
        {
            CreatedAt = DateTime.SpecifyKind(created, DateTimeKind.Utc),
            UpdatedAt = DateTime.SpecifyKind(updated, DateTimeKind.Utc),
        };

    private static IEnumerable<RawIssue> Valid(int first, int count) =>
        Enumerable.Range(first, count).Select(n => TestIssues.Valid(n));

    private static IEnumerable<RawIssue> Invalid(int first, int count) =>
        Enumerable.Range(first, count).Select(n => TestIssues.Valid(n) with { Title = "" });

    private async Task<RunRecord> RunAsync(List<RepositoryOptions>? repositories = null)
    {
        repositories ??= [new() { Owner = "microsoft", Name = "winget-cli" }];
        var step = new IngestIssuesStep(repositories, _options, _backfill, _source, _store, _watermarks, new FakeClock(Now));
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

    private sealed class FakeWatermarkStore : IWatermarkStore
    {
        public Dictionary<short, DateTime> Marks { get; } = [];

        public int AdvanceCalls { get; private set; }

        public Task<IReadOnlyDictionary<short, DateTime>> GetAllAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyDictionary<short, DateTime>>(Marks);

        public Task AdvanceAsync(long runId, CancellationToken cancellationToken)
        {
            AdvanceCalls++;
            return Task.CompletedTask;
        }
    }
}
