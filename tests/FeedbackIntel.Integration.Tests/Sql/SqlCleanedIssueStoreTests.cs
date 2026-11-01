using Dapper;

using FeedbackIntel.Core.Model;
using FeedbackIntel.Core.Text;
using FeedbackIntel.Infrastructure.Sql;

using Microsoft.Data.SqlClient;

namespace FeedbackIntel.Integration.Tests.Sql;

public class SqlCleanedIssueStoreTests
{
    private static readonly DateTime Created = new(2026, 10, 1, 9, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task UpsertAsync_NewIssue_RecordsRunAsFirstSeenAndLastChanged()
    {
        var (settings, repoId) = await CreateAsync();

        await new SqlCleanedIssueStore(settings).UpsertAsync([Issue(repoId, 1)], runId: 100, CancellationToken.None);

        var row = await ReadAsync(settings, 1);
        Assert.Equal(100, row.FirstSeenRunId);
        Assert.Equal(100, row.LastChangedRunId);
        Assert.Equal("Crash on <EMAIL_ADDRESS>", row.TitleRedacted);
    }

    [Fact]
    public async Task UpsertAsync_UnchangedIssue_KeepsLastChangedRun()
    {
        var (settings, repoId) = await CreateAsync();
        var store = new SqlCleanedIssueStore(settings);

        await store.UpsertAsync([Issue(repoId, 1)], runId: 100, CancellationToken.None);
        await store.UpsertAsync([Issue(repoId, 1)], runId: 200, CancellationToken.None);

        var row = await ReadAsync(settings, 1);
        Assert.Equal(100, row.LastChangedRunId);
        Assert.Equal(1, row.TotalIssues);
    }

    [Fact]
    public async Task UpsertAsync_ChangedIssue_UpdatesFieldsButKeepsFirstSeen()
    {
        var (settings, repoId) = await CreateAsync();
        var store = new SqlCleanedIssueStore(settings);
        var original = Issue(repoId, 1);

        await store.UpsertAsync([original], runId: 100, CancellationToken.None);
        await store.UpsertAsync(
            [original with { State = "closed", StateReason = "completed", ClosedAt = Created.AddDays(3), UpdatedAt = Created.AddDays(3) }],
            runId: 200,
            CancellationToken.None);

        var row = await ReadAsync(settings, 1);
        Assert.Equal(100, row.FirstSeenRunId);
        Assert.Equal(200, row.LastChangedRunId);
        Assert.Equal("closed", row.State);
        Assert.Equal(1, row.TotalIssues);
    }

    private static async Task<(DatabaseSettings Settings, short RepoId)> CreateAsync()
    {
        var settings = await SqlServerTestDatabase.CreateMigratedAsync();
        var repository = await new SqlIssueStore(settings)
            .EnsureRepositoryAsync("microsoft", "winget-cli", Created, CancellationToken.None);
        return (settings, repository.Id);
    }

    private static CleanedIssue Issue(short repoId, int number) => new(
        RepoId: repoId,
        IssueNumber: number,
        TitleRedacted: "Crash on <EMAIL_ADDRESS>",
        BodyRedacted: "Steps to reproduce",
        BodyLength: 18,
        BodyTruncated: false,
        ContentHash: ContentHasher.Compute("Crash on <EMAIL_ADDRESS>", "Steps to reproduce"),
        RedactionCount: 1,
        State: "open",
        StateReason: null,
        LabelsJson: """["Issue-Bug"]""",
        CreatedAt: Created,
        UpdatedAt: Created.AddHours(2),
        ClosedAt: null,
        HtmlUrl: $"https://github.com/microsoft/winget-cli/issues/{number}");

    private static async Task<CleanedRow> ReadAsync(DatabaseSettings settings, int issueNumber)
    {
        await using var connection = new SqlConnection(settings.ConnectionString);
        return await connection.QuerySingleAsync<CleanedRow>(
            """
            SELECT (SELECT COUNT(*) FROM cleaned.issue) AS TotalIssues,
                   title_redacted AS TitleRedacted, state AS State,
                   first_seen_run_id AS FirstSeenRunId, last_changed_run_id AS LastChangedRunId
            FROM cleaned.issue
            WHERE issue_number = @issueNumber
            """,
            new { issueNumber });
    }

    private sealed class CleanedRow
    {
        public int TotalIssues { get; init; }

        public string TitleRedacted { get; init; } = "";

        public string State { get; init; } = "";

        public long FirstSeenRunId { get; init; }

        public long LastChangedRunId { get; init; }
    }
}
