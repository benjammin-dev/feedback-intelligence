using Dapper;

using FeedbackIntel.Core.Model;
using FeedbackIntel.Infrastructure.Sql;

using Microsoft.Data.SqlClient;

namespace FeedbackIntel.Integration.Tests.Sql;

public class SqlIssueStoreTests
{
    private static readonly DateTime Created = new(2026, 10, 1, 9, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task EnsureRepositoryAsync_CalledTwice_ReturnsSameRepository()
    {
        var store = new SqlIssueStore(await SqlServerTestDatabase.CreateMigratedAsync());

        var first = await store.EnsureRepositoryAsync("microsoft", "winget-cli", Created, CancellationToken.None);
        var second = await store.EnsureRepositoryAsync("microsoft", "winget-cli", Created.AddDays(1), CancellationToken.None);

        Assert.Equal(first, second);
        Assert.Equal("microsoft/winget-cli", first.FullName);
    }

    [Fact]
    public async Task InsertRawIssuesAsync_StoresEveryColumn()
    {
        var settings = await SqlServerTestDatabase.CreateMigratedAsync();
        var store = new SqlIssueStore(settings);
        var repository = await store.EnsureRepositoryAsync("microsoft", "winget-cli", Created, CancellationToken.None);
        var issue = Issue(repository.Id, 4521) with
        {
            Body = null,
            State = "closed",
            StateReason = "completed",
            ClosedAt = Created.AddDays(2),
        };

        await store.InsertRawIssuesAsync([issue], CancellationToken.None);

        await using var connection = new SqlConnection(settings.ConnectionString);
        var row = await connection.QuerySingleAsync<RawIssueRow>(
            """
            SELECT issue_number AS IssueNumber, title AS Title, body AS Body, state AS State,
                   state_reason AS StateReason, labels AS Labels, closed_at AS ClosedAt, html_url AS HtmlUrl
            FROM raw.issue
            """);
        Assert.Equal(4521, row.IssueNumber);
        Assert.Equal(issue.Title, row.Title);
        Assert.Null(row.Body);
        Assert.Equal("closed", row.State);
        Assert.Equal("completed", row.StateReason);
        Assert.Equal("""["Issue-Bug","Area-CLI"]""", row.Labels);
        Assert.Equal(Created.AddDays(2), row.ClosedAt);
        Assert.Equal(issue.HtmlUrl, row.HtmlUrl);
    }

    [Fact]
    public async Task ClearRawIssuesAsync_RemovesAllRows()
    {
        var settings = await SqlServerTestDatabase.CreateMigratedAsync();
        var store = new SqlIssueStore(settings);
        var repository = await store.EnsureRepositoryAsync("microsoft", "winget-cli", Created, CancellationToken.None);
        await store.InsertRawIssuesAsync([Issue(repository.Id, 1), Issue(repository.Id, 2)], CancellationToken.None);

        await store.ClearRawIssuesAsync(CancellationToken.None);

        await using var connection = new SqlConnection(settings.ConnectionString);
        Assert.Equal(0, await connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM raw.issue"));
    }

    [Fact]
    public async Task InsertRawIssuesAsync_InvalidState_IsRejectedByDatabase()
    {
        var settings = await SqlServerTestDatabase.CreateMigratedAsync();
        var store = new SqlIssueStore(settings);
        var repository = await store.EnsureRepositoryAsync("microsoft", "winget-cli", Created, CancellationToken.None);

        await Assert.ThrowsAsync<SqlException>(
            () => store.InsertRawIssuesAsync([Issue(repository.Id, 1) with { State = "merged" }], CancellationToken.None));
    }

    private static RawIssue Issue(short repoId, int number) => new(
        RepoId: repoId,
        IssueNumber: number,
        Title: "winget upgrade fails with 0x8a15000f",
        Body: "Steps to reproduce...",
        State: "open",
        StateReason: null,
        Labels: ["Issue-Bug", "Area-CLI"],
        CreatedAt: Created,
        UpdatedAt: Created.AddHours(2),
        ClosedAt: null,
        HtmlUrl: $"https://github.com/microsoft/winget-cli/issues/{number}",
        FetchedAt: Created.AddDays(1));

    private sealed class RawIssueRow
    {
        public int IssueNumber { get; init; }

        public string Title { get; init; } = "";

        public string? Body { get; init; }

        public string State { get; init; } = "";

        public string? StateReason { get; init; }

        public string Labels { get; init; } = "";

        public DateTime? ClosedAt { get; init; }

        public string HtmlUrl { get; init; } = "";
    }
}
