using Dapper;

using FeedbackIntel.Core.Configuration;
using FeedbackIntel.Core.Model;
using FeedbackIntel.Core.Text;
using FeedbackIntel.Infrastructure.Sql;
using FeedbackIntel.Integration.Tests.Sql;

using Microsoft.Data.SqlClient;

namespace FeedbackIntel.Integration.Tests.Quality;

/// <summary>
/// Rebuilds the reporting tables from a small set of issues, then checks that healthy data
/// passes and that each kind of broken data is caught (FR-DAT-05).
/// </summary>
public class SqlQualityCheckerTests
{
    private const long RunId = 42;

    private static readonly DateTime AsOf = new(2026, 10, 24, 6, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Now = AsOf.AddMinutes(3);

    [Fact]
    public async Task FindProblems_HealthyData_ReturnsNone()
    {
        var settings = await SeedAndRebuildAsync();

        Assert.Empty(await CheckAsync(settings, Now));
    }

    [Fact]
    public async Task FindProblems_EmptyDatabase_ReportsMissingRows()
    {
        var settings = await SqlServerTestDatabase.CreateMigratedAsync();

        var problems = await CheckAsync(settings, Now);

        Assert.Contains("There are no cleaned issues to publish. (1 row)", problems);
        Assert.Contains("reporting.site_info doesn't have exactly one row from this run. (1 row)", problems);
    }

    [Fact]
    public async Task FindProblems_BrokenData_ReportsEveryFailedCheck()
    {
        var settings = await SeedAndRebuildAsync();
        await ExecuteAsync(
            settings,
            """
            UPDATE reporting.issue SET title = N'  ' WHERE issue_number = 1;
            UPDATE reporting.issue SET html_url = 'https://example.com/x' WHERE issue_number IN (2, 3);
            DELETE FROM reporting.overview WHERE repository = 'all';
            UPDATE reporting.site_info SET last_run_id = 1;
            """);

        var problems = await CheckAsync(settings, Now);

        Assert.Equal<string>(
            [
                "reporting.overview doesn't have a current and a previous row for every repository and 'all'. (2 rows)",
                "reporting.site_info doesn't have exactly one row from this run. (1 row)",
                "reporting.issue has rows with an empty title. (2 rows)",
                "reporting.issue has rows whose link isn't a GitHub issue link. (2 rows)",
            ],
            problems);
    }

    [Fact]
    public async Task FindProblems_IssueMissingFromReporting_ReportsCountMismatch()
    {
        var settings = await SeedAndRebuildAsync();
        await ExecuteAsync(settings, "DELETE TOP (1) FROM reporting.issue");

        Assert.Equal<string>(
            ["reporting.issue doesn't have exactly one row per cleaned issue. (1 row)"],
            await CheckAsync(settings, Now));
    }

    [Fact]
    public async Task FindProblems_InvalidValues_AreReported()
    {
        var settings = await SeedAndRebuildAsync();
        await ExecuteAsync(
            settings,
            """
            UPDATE reporting.issue SET state = 'merged' WHERE issue_number = 1 AND repository = 'microsoft/winget-cli';
            UPDATE reporting.issue SET closed_at = NULL, days_to_close = -1 WHERE issue_number = 2 AND repository = 'microsoft/winget-cli';
            UPDATE TOP (1) reporting.weekly_metrics SET week_start = DATEADD(day, 1, week_start), new_issues = -1;
            """);

        Assert.Equal<string>(
            [
                "reporting.issue has closed issues without a closing time. (1 row)",
                "reporting.issue has rows with a state other than open or closed. (1 row)",
                "reporting.issue has issues closed before they were created. (1 row)",
                "reporting.weekly_metrics has weeks that don't start on a Monday. (1 row)",
                "reporting.weekly_metrics has negative counts. (1 row)",
            ],
            await CheckAsync(settings, Now));
    }

    [Fact]
    public async Task FindProblems_OldReportingData_IsReportedAsStale()
    {
        var settings = await SeedAndRebuildAsync();

        Assert.Equal<string>(
            ["reporting.site_info isn't from the last 24 hours. (1 row)"],
            await CheckAsync(settings, AsOf.AddDays(2)));
    }

    [Fact]
    public async Task FindProblems_NeverQuoteData()
    {
        var settings = await SeedAndRebuildAsync();
        await ExecuteAsync(settings, "DELETE FROM reporting.issue; DELETE FROM reporting.overview;");

        var problems = await CheckAsync(settings, Now);

        Assert.NotEmpty(problems);
        Assert.DoesNotContain(problems, problem => problem.Contains("Test issue", StringComparison.Ordinal));
        Assert.DoesNotContain(problems, problem => problem.Contains("winget", StringComparison.Ordinal));
    }

    private static Task<IReadOnlyList<string>> CheckAsync(DatabaseSettings settings, DateTime now) =>
        new SqlQualityChecker(settings).FindProblemsAsync(RunId, now, CancellationToken.None);

    private static async Task ExecuteAsync(DatabaseSettings settings, string sql)
    {
        await using var connection = new SqlConnection(settings.ConnectionString);
        await connection.ExecuteAsync(sql);
    }

    private static async Task<DatabaseSettings> SeedAndRebuildAsync()
    {
        var settings = await SqlServerTestDatabase.CreateMigratedAsync();
        var store = new SqlIssueStore(settings);
        var winget = await store.EnsureRepositoryAsync("microsoft", "winget-cli", AsOf, CancellationToken.None);
        var powerToys = await store.EnsureRepositoryAsync("microsoft", "PowerToys", AsOf, CancellationToken.None);

        await new SqlCleanedIssueStore(settings).UpsertAsync(
            [
                Issue(winget.Id, 1, created: Day(20)),
                Issue(winget.Id, 2, created: Day(18), closed: Day(20)),
                Issue(winget.Id, 3, created: Day(12), closed: Day(16)),
                Issue(powerToys.Id, 1, created: Day(21)),
            ],
            RunId,
            CancellationToken.None);

        await new SqlReportingBuilder(settings, new IngestionOptions { InitialHistoryDays = 90 })
            .RebuildAsync(AsOf, RunId, CancellationToken.None);
        return settings;
    }

    /// <summary>Noon UTC on the given day of October 2026.</summary>
    private static DateTime Day(int day) => new(2026, 10, day, 12, 0, 0, DateTimeKind.Utc);

    private static CleanedIssue Issue(short repoId, int number, DateTime created, DateTime? closed = null) => new(
        RepoId: repoId,
        IssueNumber: number,
        TitleRedacted: "Test issue",
        BodyRedacted: null,
        BodyLength: 0,
        BodyTruncated: false,
        ContentHash: ContentHasher.Compute("Test issue", null),
        RedactionCount: 0,
        State: closed is null ? "open" : "closed",
        StateReason: closed is null ? null : "completed",
        LabelsJson: "[]",
        CreatedAt: created,
        UpdatedAt: closed ?? created,
        ClosedAt: closed,
        HtmlUrl: $"https://github.com/microsoft/test/issues/{number}");
}
