using System.Data;

using Dapper;

using FeedbackIntel.Core.Ingestion;
using FeedbackIntel.Core.Model;

using Microsoft.Data.SqlClient;

namespace FeedbackIntel.Infrastructure.Sql;

/// <summary>Stores repositories in <c>cleaned.repository</c> and fetched issues in <c>raw.issue</c>.</summary>
public sealed class SqlIssueStore(DatabaseSettings settings) : IIssueStore
{
    /// <inheritdoc />
    public async Task<TrackedRepository> EnsureRepositoryAsync(
        string owner, string name, DateTime onboardedAt, CancellationToken cancellationToken)
    {
        const string sql = """
            IF NOT EXISTS (SELECT 1 FROM cleaned.repository WHERE owner = @owner AND name = @name)
                INSERT INTO cleaned.repository (owner, name, onboarded_at) VALUES (@owner, @name, @onboardedAt);

            SELECT repo_id AS Id, owner AS Owner, name AS Name
            FROM cleaned.repository
            WHERE owner = @owner AND name = @name;
            """;

        await using var connection = new SqlConnection(settings.ConnectionString);
        return await connection.QuerySingleAsync<TrackedRepository>(
            new CommandDefinition(sql, new { owner, name, onboardedAt }, cancellationToken: cancellationToken));
    }

    /// <inheritdoc />
    public async Task ClearRawIssuesAsync(CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(settings.ConnectionString);
        await connection.ExecuteAsync(
            new CommandDefinition("TRUNCATE TABLE raw.issue;", cancellationToken: cancellationToken));
    }

    /// <inheritdoc />
    /// <remarks>
    /// Uses SqlBulkCopy, which sends all rows in one stream instead of one INSERT per row.
    /// <see cref="SqlBulkCopyOptions.CheckConstraints"/> is set because bulk copy skips
    /// check and foreign key constraints by default.
    /// </remarks>
    public async Task InsertRawIssuesAsync(IReadOnlyCollection<RawIssue> issues, CancellationToken cancellationToken)
    {
        if (issues.Count == 0)
        {
            return;
        }

        using var table = ToDataTable(issues);

        await using var connection = new SqlConnection(settings.ConnectionString);
        await connection.OpenAsync(cancellationToken);

        using var bulkCopy = new SqlBulkCopy(connection, SqlBulkCopyOptions.CheckConstraints, null)
        {
            DestinationTableName = "raw.issue",
        };

        foreach (DataColumn column in table.Columns)
        {
            bulkCopy.ColumnMappings.Add(column.ColumnName, column.ColumnName);
        }

        await bulkCopy.WriteToServerAsync(table, cancellationToken);
    }

    private static DataTable ToDataTable(IEnumerable<RawIssue> issues)
    {
        var table = new DataTable();
        table.Columns.Add("repo_id", typeof(short));
        table.Columns.Add("issue_number", typeof(int));
        table.Columns.Add("title", typeof(string));
        table.Columns.Add("body", typeof(string));
        table.Columns.Add("state", typeof(string));
        table.Columns.Add("state_reason", typeof(string));
        table.Columns.Add("labels", typeof(string));
        table.Columns.Add("created_at", typeof(DateTime));
        table.Columns.Add("updated_at", typeof(DateTime));
        table.Columns.Add("closed_at", typeof(DateTime));
        table.Columns.Add("html_url", typeof(string));
        table.Columns.Add("fetched_at", typeof(DateTime));

        foreach (var issue in issues)
        {
            table.Rows.Add(
                issue.RepoId,
                issue.IssueNumber,
                issue.Title,
                (object?)issue.Body ?? DBNull.Value,
                issue.State,
                (object?)issue.StateReason ?? DBNull.Value,
                issue.LabelsJson,
                issue.CreatedAt,
                issue.UpdatedAt,
                (object?)issue.ClosedAt ?? DBNull.Value,
                issue.HtmlUrl,
                issue.FetchedAt);
        }

        return table;
    }
}
