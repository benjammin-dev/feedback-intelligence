using Dapper;

using FeedbackIntel.Core.Configuration;
using FeedbackIntel.Core.Model;
using FeedbackIntel.Core.Orchestration;
using FeedbackIntel.Core.Text;
using FeedbackIntel.Infrastructure.History;
using FeedbackIntel.Infrastructure.ParquetFiles;
using FeedbackIntel.Infrastructure.Sql;
using FeedbackIntel.Integration.Tests.Sql;

using Microsoft.Data.SqlClient;

namespace FeedbackIntel.Integration.Tests.History;

/// <summary>
/// Saves history from one database and restores it into a fresh one, as a daily run does,
/// then checks that everything came back exactly, and that issue bodies never left.
/// </summary>
public class ParquetHistoryStoreTests : IDisposable
{
    private static readonly DateTime Created = new(2026, 10, 1, 9, 30, 15, DateTimeKind.Utc);

    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"fip-history-{Guid.NewGuid():N}");

    [Fact]
    public async Task SaveThenRestore_BringsBackEveryTable()
    {
        var source = await SeedAsync();
        await Store(source).SaveAsync(CancellationToken.None);

        var target = await SqlServerTestDatabase.CreateMigratedAsync();
        await Store(target).RestoreAsync(CancellationToken.None);

        await using var from = new SqlConnection(source.ConnectionString);
        await using var to = new SqlConnection(target.ConnectionString);
        foreach (var query in ComparisonQueries)
        {
            Assert.Equal(await RowsAsync(from, query), await RowsAsync(to, query));
        }
    }

    [Fact]
    public async Task SaveThenRestore_KeepsTimestampsInUtc()
    {
        var target = await SaveAndRestoreAsync();

        await using var connection = new SqlConnection(target.ConnectionString);
        var createdAt = await connection.ExecuteScalarAsync<DateTime>("SELECT created_at FROM cleaned.issue WHERE issue_number = 1");
        Assert.Equal(Created, createdAt);
    }

    [Fact]
    public async Task SaveThenRestore_NeverIncludesIssueBodies()
    {
        var target = await SaveAndRestoreAsync();

        Assert.DoesNotContain("body_redacted", ParquetTable<IssueRow>.ColumnNames);
        await using var connection = new SqlConnection(target.ConnectionString);
        Assert.Equal(0, await connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM cleaned.issue WHERE body_redacted IS NOT NULL"));
    }

    [Fact]
    public async Task Restore_KeepsRepositoryIds_AndNewRepositoriesContinueAfterThem()
    {
        var target = await SaveAndRestoreAsync();

        var store = new SqlIssueStore(target);
        var existing = await store.EnsureRepositoryAsync("microsoft", "PowerToys", Created, CancellationToken.None);
        var added = await store.EnsureRepositoryAsync("microsoft", "vscode", Created, CancellationToken.None);
        Assert.Equal(2, existing.Id);
        Assert.Equal(3, added.Id);
    }

    [Fact]
    public async Task HistoryExists_OnlyAfterSave()
    {
        var source = await SeedAsync();
        var store = Store(source);

        Assert.False(store.HistoryExists());
        await store.SaveAsync(CancellationToken.None);
        Assert.True(store.HistoryExists());
    }

    [Fact]
    public async Task DatabaseHasData_ReflectsRepositories()
    {
        var empty = await SqlServerTestDatabase.CreateMigratedAsync();
        var seeded = await SeedAsync();

        Assert.False(await Store(empty).DatabaseHasDataAsync(CancellationToken.None));
        Assert.True(await Store(seeded).DatabaseHasDataAsync(CancellationToken.None));
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }

        GC.SuppressFinalize(this);
    }

    private static readonly string[] ComparisonQueries =
    [
        "SELECT repo_id, owner, name, onboarded_at FROM cleaned.repository ORDER BY repo_id",
        """
        SELECT repo_id, issue_number, title_redacted, body_length, body_truncated, CONVERT(varchar(64), content_hash, 2),
               redaction_count, state, state_reason, labels, created_at, updated_at, closed_at, html_url,
               first_seen_run_id, last_changed_run_id
        FROM cleaned.issue ORDER BY repo_id, issue_number
        """,
        """
        SELECT run_id, [trigger], started_at, finished_at, status, issues_fetched, issues_rejected, issues_redacted,
               ai_requests, ai_cache_hits, issues_unclassified, failed_step, error_message
        FROM ops.run ORDER BY run_id
        """,
        "SELECT repo_id, last_updated_at, last_success_run_id FROM ops.watermark ORDER BY repo_id",
    ];

    private async Task<DatabaseSettings> SaveAndRestoreAsync()
    {
        await Store(await SeedAsync()).SaveAsync(CancellationToken.None);
        var target = await SqlServerTestDatabase.CreateMigratedAsync();
        await Store(target).RestoreAsync(CancellationToken.None);
        return target;
    }

    private ParquetHistoryStore Store(DatabaseSettings settings) =>
        new(settings, new PublishingOptions { HistoryDirectory = _directory });

    /// <summary>Two repositories, two issues (one closed, one with a body), two runs, and a watermark.</summary>
    private static async Task<DatabaseSettings> SeedAsync()
    {
        var settings = await SqlServerTestDatabase.CreateMigratedAsync();
        var issues = new SqlIssueStore(settings);
        var winget = await issues.EnsureRepositoryAsync("microsoft", "winget-cli", Created, CancellationToken.None);
        await issues.EnsureRepositoryAsync("microsoft", "PowerToys", Created.AddDays(1), CancellationToken.None);

        await new SqlCleanedIssueStore(settings).UpsertAsync(
            [
                Issue(winget.Id, 1, body: "Steps to reproduce", closedAt: null),
                Issue(winget.Id, 2, body: null, closedAt: Created.AddDays(3)),
            ],
            runId: 100,
            CancellationToken.None);

        var runs = new SqlRunStore(settings);
        await runs.SaveAsync(
            new RunRecord { RunId = 100, Trigger = "schedule", StartedAt = Created }.AsSucceeded(Created.AddMinutes(1)),
            CancellationToken.None);
        await runs.SaveAsync(
            new RunRecord
            {
                RunId = 101,
                Trigger = "manual",
                StartedAt = Created.AddDays(1),
                FinishedAt = Created.AddDays(1).AddMinutes(1),
                Status = RunStatus.Failed,
                FailedStep = "ingest issues",
                ErrorMessage = "HttpRequestException: GitHub returned 502",
            },
            CancellationToken.None);

        await using var connection = new SqlConnection(settings.ConnectionString);
        await connection.ExecuteAsync(
            "INSERT INTO ops.watermark (repo_id, last_updated_at, last_success_run_id) VALUES (@repoId, @at, 100)",
            new { repoId = winget.Id, at = Created.AddDays(2) });

        return settings;
    }

    private static CleanedIssue Issue(short repoId, int number, string? body, DateTime? closedAt) => new(
        RepoId: repoId,
        IssueNumber: number,
        TitleRedacted: $"Crash number {number} on <EMAIL_ADDRESS>",
        BodyRedacted: body,
        BodyLength: body?.Length ?? 0,
        BodyTruncated: false,
        ContentHash: ContentHasher.Compute($"Crash number {number} on <EMAIL_ADDRESS>", body),
        RedactionCount: 1,
        State: closedAt is null ? "open" : "closed",
        StateReason: closedAt is null ? null : "completed",
        LabelsJson: """["Issue-Bug"]""",
        CreatedAt: Created,
        UpdatedAt: closedAt ?? Created.AddHours(2),
        ClosedAt: closedAt,
        HtmlUrl: $"https://github.com/microsoft/winget-cli/issues/{number}");

    /// <summary>Reads every row of a query as text, so two databases can be compared exactly.</summary>
    private static async Task<List<string>> RowsAsync(SqlConnection connection, string query)
    {
        var rows = await connection.QueryAsync(query);
        return rows
            .Select(row => string.Join("|", ((IDictionary<string, object?>)row).Values.Select(value => value switch
            {
                DateTime time => time.ToString("O"),
                null => "NULL",
                _ => value.ToString(),
            })))
            .ToList();
    }
}
