using Dapper;

using FeedbackIntel.Core.Orchestration;
using FeedbackIntel.Infrastructure.Sql;

using Microsoft.Data.SqlClient;

namespace FeedbackIntel.Integration.Tests.Sql;

public class SqlRunStoreTests
{
    private static readonly DateTime Start = new(2026, 10, 20, 6, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task SaveAsync_InsertThenUpdate_StoresLatestValues()
    {
        var settings = await SqlServerTestDatabase.CreateMigratedAsync();
        var store = new SqlRunStore(settings);
        var run = new RunRecord { RunId = 12_345_678_901, Trigger = "schedule", StartedAt = Start };

        await store.SaveAsync(run, CancellationToken.None);
        run.Status = RunStatus.Succeeded;
        run.FinishedAt = Start.AddMinutes(4);
        run.IssuesFetched = 250;
        await store.SaveAsync(run, CancellationToken.None);

        var row = await ReadRunAsync(settings, run.RunId);
        Assert.Equal(1, row.TotalRuns);
        Assert.Equal("schedule", row.Trigger);
        Assert.Equal("succeeded", row.Status);
        Assert.Equal(Start, row.StartedAt);
        Assert.Equal(Start.AddMinutes(4), row.FinishedAt);
        Assert.Equal(250, row.IssuesFetched);
        Assert.Null(row.FailedStep);
    }

    [Fact]
    public async Task SaveAsync_FailedRun_StoresFailedStepAndMessage()
    {
        var settings = await SqlServerTestDatabase.CreateMigratedAsync();
        var run = new RunRecord
        {
            RunId = 7,
            Trigger = "manual",
            StartedAt = Start,
            FinishedAt = Start.AddMinutes(1),
            Status = RunStatus.Failed,
            FailedStep = "ingest issues",
            ErrorMessage = "HttpRequestException: GitHub returned 502",
        };

        await new SqlRunStore(settings).SaveAsync(run, CancellationToken.None);

        var row = await ReadRunAsync(settings, run.RunId);
        Assert.Equal("failed", row.Status);
        Assert.Equal("ingest issues", row.FailedStep);
        Assert.Equal("HttpRequestException: GitHub returned 502", row.ErrorMessage);
    }

    private static async Task<RunRow> ReadRunAsync(DatabaseSettings settings, long runId)
    {
        await using var connection = new SqlConnection(settings.ConnectionString);
        return await connection.QuerySingleAsync<RunRow>(
            """
            SELECT (SELECT COUNT(*) FROM ops.run) AS TotalRuns,
                   [trigger] AS [Trigger], status AS Status, started_at AS StartedAt,
                   finished_at AS FinishedAt, issues_fetched AS IssuesFetched,
                   failed_step AS FailedStep, error_message AS ErrorMessage
            FROM ops.run
            WHERE run_id = @runId
            """,
            new { runId });
    }

    private sealed class RunRow
    {
        public int TotalRuns { get; init; }

        public string Trigger { get; init; } = "";

        public string Status { get; init; } = "";

        public DateTime StartedAt { get; init; }

        public DateTime? FinishedAt { get; init; }

        public int IssuesFetched { get; init; }

        public string? FailedStep { get; init; }

        public string? ErrorMessage { get; init; }
    }
}
