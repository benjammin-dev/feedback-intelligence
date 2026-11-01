using System.Text.Json;

using Dapper;

using FeedbackIntel.Core.Configuration;
using FeedbackIntel.Core.Model;
using FeedbackIntel.Core.Orchestration;
using FeedbackIntel.Core.Text;
using FeedbackIntel.Infrastructure.Publishing;
using FeedbackIntel.Infrastructure.Sql;
using FeedbackIntel.Integration.Tests.Sql;

using Microsoft.Data.SqlClient;

namespace FeedbackIntel.Integration.Tests.Reporting;

/// <summary>
/// Builds a small known set of cleaned issues, then checks the reporting procedure's numbers
/// and the published JSON files against values worked out by hand.
/// </summary>
public class ReportingAndPublishingTests
{
    // Current window: Oct 17 06:00 to Oct 24 06:00. Previous window: Oct 10 06:00 to Oct 17 06:00.
    private static readonly DateTime AsOf = new(2026, 10, 24, 6, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task Rebuild_CountsNewAndClosedIssuesPerPeriod()
    {
        var settings = await SeedAndRebuildAsync();

        var winget = await ReadOverviewAsync(settings, "microsoft/winget-cli");
        Assert.Equal((2, 3, (decimal?)7.0m), winget["current"]);
        Assert.Equal((2, 1, (decimal?)4.0m), winget["previous"]);
    }

    [Fact]
    public async Task Rebuild_AllRowCombinesRepositories()
    {
        var settings = await SeedAndRebuildAsync();

        var all = await ReadOverviewAsync(settings, "all");
        Assert.Equal((3, 3, (decimal?)7.0m), all["current"]);
        Assert.Equal((2, 1, (decimal?)4.0m), all["previous"]);
    }

    [Fact]
    public async Task Rebuild_NothingClosed_LeavesMedianEmpty()
    {
        var settings = await SeedAndRebuildAsync();

        var powerToys = await ReadOverviewAsync(settings, "microsoft/PowerToys");
        Assert.Equal((1, 0, (decimal?)null), powerToys["current"]);
    }

    [Fact]
    public async Task Publish_WritesContractColumnsOnly()
    {
        var settings = await SeedAndRebuildAsync();
        var output = Path.Combine(Path.GetTempPath(), $"fip-test-{Guid.NewGuid():N}");

        try
        {
            await new SitePublisher(settings, new PublishingOptions { OutputDirectory = output })
                .PublishAsync(CancellationToken.None);

            using var overview = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(output, "data", "overview.json")));
            Assert.Equal(6, overview.RootElement.GetArrayLength());
            Assert.Equal<string>(
                ["repository", "period", "period_start", "period_end", "new_issues", "closed_issues",
                 "bug_count", "critical_count", "median_days_to_close"],
                overview.RootElement[0].EnumerateObject().Select(property => property.Name));

            using var siteInfo = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(output, "data", "site_info.json")));
            Assert.Equal("2026-10-24T06:00:00Z", siteInfo.RootElement.GetProperty("data_updated_at").GetString());
            Assert.Equal(42, siteInfo.RootElement.GetProperty("last_run_id").GetInt64());
            Assert.Equal<string>(
                ["microsoft/PowerToys", "microsoft/winget-cli"],
                JsonSerializer.Deserialize<string[]>(siteInfo.RootElement.GetProperty("repositories").GetString()!)!);
        }
        finally
        {
            if (Directory.Exists(output))
            {
                Directory.Delete(output, recursive: true);
            }
        }
    }

    [Fact]
    public async Task Rebuild_IssueTable_HasRedactedTitleAndDaysToClose()
    {
        var settings = await SeedAndRebuildAsync();

        await using var connection = new SqlConnection(settings.ConnectionString);
        var row = await connection.QuerySingleAsync<(string Title, decimal? DaysToClose, string AiStatus, string? Category)>(
            """
            SELECT title, days_to_close, ai_status, category
            FROM reporting.issue
            WHERE repository = 'microsoft/winget-cli' AND issue_number = 4
            """);
        Assert.Equal("Test issue", row.Title);
        Assert.Equal(7.0m, row.DaysToClose);
        Assert.Equal("pending", row.AiStatus);
        Assert.Null(row.Category);
        Assert.Equal(6, await connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM reporting.issue"));
    }

    [Fact]
    public async Task Rebuild_WeeklyMetrics_CoversCompleteWeeksInTheHistoryWindow()
    {
        var settings = await SeedAndRebuildAsync();

        await using var connection = new SqlConnection(settings.ConnectionString);
        var weeks = (await connection.QueryAsync<(DateTime WeekStart, int New, int Closed, decimal? Median)>(
            """
            SELECT week_start, new_issues, closed_issues, median_days_to_close
            FROM reporting.weekly_metrics
            WHERE repository = 'microsoft/winget-cli'
            ORDER BY week_start
            """)).ToList();

        // AsOf is Saturday Oct 24, so the current week (from Monday Oct 19) is left out.
        // The 90-day window starts Sunday Jul 26, so the first full week starts Monday Jul 27.
        Assert.Equal(12, weeks.Count);
        Assert.Equal(new DateTime(2026, 7, 27), weeks[0].WeekStart);
        Assert.Equal(new DateTime(2026, 10, 12), weeks[^1].WeekStart);
        Assert.Equal((2, 2, (decimal?)5.5m), (weeks[^1].New, weeks[^1].Closed, weeks[^1].Median));
        Assert.Equal((1, 0, (decimal?)null), (weeks[^2].New, weeks[^2].Closed, weeks[^2].Median));
    }

    [Fact]
    public async Task Rebuild_WeeklyMetrics_AllRowMatchesSingleRepositoryHere()
    {
        var settings = await SeedAndRebuildAsync();

        await using var connection = new SqlConnection(settings.ConnectionString);
        var all = await connection.ExecuteScalarAsync<int>(
            "SELECT new_issues FROM reporting.weekly_metrics WHERE repository = 'all' AND week_start = '2026-10-12'");
        Assert.Equal(2, all);
    }

    [Fact]
    public async Task Rebuild_RunHealth_ListsFinishedRunsWithDuration()
    {
        var settings = await SeedAndRebuildAsync();

        await using var connection = new SqlConnection(settings.ConnectionString);
        var run = await connection.QuerySingleAsync<(long RunId, int? Duration, string Status)>(
            "SELECT run_id, duration_seconds, status FROM reporting.run_health");
        Assert.Equal((41L, (int?)90, "succeeded"), run);
    }

    private static async Task<DatabaseSettings> SeedAndRebuildAsync()
    {
        var settings = await SqlServerTestDatabase.CreateMigratedAsync();
        var store = new SqlIssueStore(settings);
        var winget = await store.EnsureRepositoryAsync("microsoft", "winget-cli", AsOf, CancellationToken.None);
        var powerToys = await store.EnsureRepositoryAsync("microsoft", "PowerToys", AsOf, CancellationToken.None);

        await new SqlCleanedIssueStore(settings).UpsertAsync(
        [
            Issue(winget.Id, 1, created: Day(20)),                  // new (current), still open
            Issue(winget.Id, 2, created: Day(18), closed: Day(20)), // new and closed (current), 2 days
            Issue(winget.Id, 3, created: Day(12), closed: Day(16)), // new and closed (previous), 4 days
            Issue(winget.Id, 4, created: Day(11), closed: Day(18)), // new (previous), closed (current), 7 days
            Issue(winget.Id, 5, created: Day(1), closed: Day(19)),  // closed (current), 18 days
            Issue(powerToys.Id, 1, created: Day(21)),               // new (current), still open
        ],
            runId: 42,
            CancellationToken.None);

        await new SqlRunStore(settings).SaveAsync(
            new RunRecord { RunId = 41, Trigger = "schedule", StartedAt = AsOf.AddDays(-1) }.AsSucceeded(AsOf.AddDays(-1).AddSeconds(90)),
            CancellationToken.None);

        await new SqlReportingBuilder(settings, new IngestionOptions { InitialHistoryDays = 90 }).RebuildAsync(AsOf, 42, CancellationToken.None);
        return settings;
    }

    private static async Task<Dictionary<string, (int New, int Closed, decimal? Median)>> ReadOverviewAsync(
        DatabaseSettings settings, string repository)
    {
        await using var connection = new SqlConnection(settings.ConnectionString);
        var rows = await connection.QueryAsync<(string Period, int New, int Closed, decimal? Median)>(
            """
            SELECT period, new_issues, closed_issues, median_days_to_close
            FROM reporting.overview
            WHERE repository = @repository
            """,
            new { repository });
        return rows.ToDictionary(row => row.Period, row => (row.New, row.Closed, row.Median));
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
