using Dapper;

using FeedbackIntel.Core.Model;
using FeedbackIntel.Infrastructure.Sql;

using Microsoft.Data.SqlClient;

namespace FeedbackIntel.Integration.Tests.Sql;

public class SqlWatermarkStoreTests
{
    private static readonly DateTime Created = new(2026, 10, 1, 9, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task GetAllAsync_NoWatermarks_ReturnsEmpty()
    {
        var settings = await SqlServerTestDatabase.CreateMigratedAsync();

        var marks = await new SqlWatermarkStore(settings).GetAllAsync(CancellationToken.None);

        Assert.Empty(marks);
    }

    [Fact]
    public async Task AdvanceAsync_FirstRun_SetsNewestUpdateTimePerRepository()
    {
        var (settings, issues, watermarks) = await CreateAsync();
        var winget = await issues.EnsureRepositoryAsync("microsoft", "winget-cli", Created, CancellationToken.None);
        var powerToys = await issues.EnsureRepositoryAsync("microsoft", "PowerToys", Created, CancellationToken.None);
        await issues.InsertRawIssuesAsync(
            [Issue(winget.Id, 1, updated: Day(10)), Issue(winget.Id, 2, updated: Day(12)), Issue(powerToys.Id, 1, updated: Day(11))],
            CancellationToken.None);

        await watermarks.AdvanceAsync(runId: 5, CancellationToken.None);

        var marks = await watermarks.GetAllAsync(CancellationToken.None);
        Assert.Equal(Day(12), marks[winget.Id]);
        Assert.Equal(Day(11), marks[powerToys.Id]);
        Assert.Equal(DateTimeKind.Utc, marks[winget.Id].Kind);
        Assert.Equal(5, await RunIdAsync(settings, winget.Id));
    }

    [Fact]
    public async Task AdvanceAsync_NewerIssues_MovesForward()
    {
        var (settings, issues, watermarks) = await CreateAsync();
        var repo = await issues.EnsureRepositoryAsync("microsoft", "winget-cli", Created, CancellationToken.None);
        await issues.InsertRawIssuesAsync([Issue(repo.Id, 1, updated: Day(10))], CancellationToken.None);
        await watermarks.AdvanceAsync(runId: 5, CancellationToken.None);

        await issues.ClearRawIssuesAsync(CancellationToken.None);
        await issues.InsertRawIssuesAsync([Issue(repo.Id, 2, updated: Day(15))], CancellationToken.None);
        await watermarks.AdvanceAsync(runId: 6, CancellationToken.None);

        Assert.Equal(Day(15), (await watermarks.GetAllAsync(CancellationToken.None))[repo.Id]);
        Assert.Equal(6, await RunIdAsync(settings, repo.Id));
    }

    [Fact]
    public async Task AdvanceAsync_OlderIssues_NeverMovesBackward()
    {
        var (settings, issues, watermarks) = await CreateAsync();
        var repo = await issues.EnsureRepositoryAsync("microsoft", "winget-cli", Created, CancellationToken.None);
        await issues.InsertRawIssuesAsync([Issue(repo.Id, 1, updated: Day(15))], CancellationToken.None);
        await watermarks.AdvanceAsync(runId: 5, CancellationToken.None);

        await issues.ClearRawIssuesAsync(CancellationToken.None);
        await issues.InsertRawIssuesAsync([Issue(repo.Id, 2, updated: Day(10))], CancellationToken.None);
        await watermarks.AdvanceAsync(runId: 6, CancellationToken.None);

        Assert.Equal(Day(15), (await watermarks.GetAllAsync(CancellationToken.None))[repo.Id]);
        Assert.Equal(5, await RunIdAsync(settings, repo.Id));
    }

    [Fact]
    public async Task AdvanceAsync_NothingFetched_KeepsWatermark()
    {
        var (_, issues, watermarks) = await CreateAsync();
        var repo = await issues.EnsureRepositoryAsync("microsoft", "winget-cli", Created, CancellationToken.None);
        await issues.InsertRawIssuesAsync([Issue(repo.Id, 1, updated: Day(15))], CancellationToken.None);
        await watermarks.AdvanceAsync(runId: 5, CancellationToken.None);

        await issues.ClearRawIssuesAsync(CancellationToken.None);
        await watermarks.AdvanceAsync(runId: 6, CancellationToken.None);

        Assert.Equal(Day(15), (await watermarks.GetAllAsync(CancellationToken.None))[repo.Id]);
    }

    private static async Task<(DatabaseSettings Settings, SqlIssueStore Issues, SqlWatermarkStore Watermarks)> CreateAsync()
    {
        var settings = await SqlServerTestDatabase.CreateMigratedAsync();
        return (settings, new SqlIssueStore(settings), new SqlWatermarkStore(settings));
    }

    private static async Task<long> RunIdAsync(DatabaseSettings settings, short repoId)
    {
        await using var connection = new SqlConnection(settings.ConnectionString);
        return await connection.ExecuteScalarAsync<long>(
            "SELECT last_success_run_id FROM ops.watermark WHERE repo_id = @repoId", new { repoId });
    }

    /// <summary>Noon UTC on the given day of October 2026.</summary>
    private static DateTime Day(int day) => new(2026, 10, day, 12, 0, 0, DateTimeKind.Utc);

    private static RawIssue Issue(short repoId, int number, DateTime updated) => new(
        RepoId: repoId,
        IssueNumber: number,
        Title: "Test issue",
        Body: null,
        State: "open",
        StateReason: null,
        Labels: [],
        CreatedAt: Created,
        UpdatedAt: updated,
        ClosedAt: null,
        HtmlUrl: $"https://github.com/microsoft/test/issues/{number}",
        FetchedAt: updated);
}
