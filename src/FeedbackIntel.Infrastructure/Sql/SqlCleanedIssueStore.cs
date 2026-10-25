using System.Data;

using Dapper;

using FeedbackIntel.Core.Model;
using FeedbackIntel.Core.Privacy;

using Microsoft.Data.SqlClient;

namespace FeedbackIntel.Infrastructure.Sql;

/// <summary>
/// Upserts redacted issues into <c>cleaned.issue</c>: rows are bulk-copied into a temporary
/// staging table, then merged in one statement (FR-DAT-03).
/// </summary>
public sealed class SqlCleanedIssueStore(DatabaseSettings settings) : ICleanedIssueStore
{
    private const string CreateStagingSql = """
        CREATE TABLE #stage
        (
            repo_id         smallint       NOT NULL,
            issue_number    int            NOT NULL,
            title_redacted  nvarchar(1000) NOT NULL,
            body_redacted   nvarchar(4000) NULL,
            body_length     int            NOT NULL,
            body_truncated  bit            NOT NULL,
            content_hash    binary(32)     NOT NULL,
            redaction_count smallint       NOT NULL,
            state           varchar(10)    NOT NULL,
            state_reason    varchar(20)    NULL,
            labels          nvarchar(2000) NOT NULL,
            created_at      datetime2(0)   NOT NULL,
            updated_at      datetime2(0)   NOT NULL,
            closed_at       datetime2(0)   NULL,
            html_url        varchar(300)   NOT NULL,
            PRIMARY KEY (repo_id, issue_number)
        );
        """;

    // An issue only counts as changed if GitHub's update time or the redacted content changed,
    // so last_changed_run_id points at the run that actually changed it.
    private const string MergeSql = """
        MERGE cleaned.issue WITH (HOLDLOCK) AS target
        USING #stage AS source
            ON target.repo_id = source.repo_id AND target.issue_number = source.issue_number
        WHEN MATCHED AND (target.updated_at <> source.updated_at OR target.content_hash <> source.content_hash) THEN
            UPDATE SET
                title_redacted = source.title_redacted,
                body_redacted = source.body_redacted,
                body_length = source.body_length,
                body_truncated = source.body_truncated,
                content_hash = source.content_hash,
                redaction_count = source.redaction_count,
                state = source.state,
                state_reason = source.state_reason,
                labels = source.labels,
                created_at = source.created_at,
                updated_at = source.updated_at,
                closed_at = source.closed_at,
                html_url = source.html_url,
                last_changed_run_id = @runId
        WHEN NOT MATCHED BY TARGET THEN
            INSERT (repo_id, issue_number, title_redacted, body_redacted, body_length, body_truncated,
                    content_hash, redaction_count, state, state_reason, labels, created_at, updated_at,
                    closed_at, html_url, first_seen_run_id, last_changed_run_id)
            VALUES (source.repo_id, source.issue_number, source.title_redacted, source.body_redacted,
                    source.body_length, source.body_truncated, source.content_hash, source.redaction_count,
                    source.state, source.state_reason, source.labels, source.created_at, source.updated_at,
                    source.closed_at, source.html_url, @runId, @runId);
        """;

    /// <inheritdoc />
    public async Task UpsertAsync(IReadOnlyCollection<CleanedIssue> issues, long runId, CancellationToken cancellationToken)
    {
        if (issues.Count == 0)
        {
            return;
        }

        using var table = ToDataTable(issues);

        // The temporary table only exists on this connection, so every command shares it.
        await using var connection = new SqlConnection(settings.ConnectionString);
        await connection.OpenAsync(cancellationToken);
        await connection.ExecuteAsync(new CommandDefinition(CreateStagingSql, cancellationToken: cancellationToken));

        using (var bulkCopy = new SqlBulkCopy(connection) { DestinationTableName = "#stage" })
        {
            foreach (DataColumn column in table.Columns)
            {
                bulkCopy.ColumnMappings.Add(column.ColumnName, column.ColumnName);
            }

            await bulkCopy.WriteToServerAsync(table, cancellationToken);
        }

        await connection.ExecuteAsync(new CommandDefinition(MergeSql, new { runId }, cancellationToken: cancellationToken));
    }

    private static DataTable ToDataTable(IEnumerable<CleanedIssue> issues)
    {
        var table = new DataTable();
        table.Columns.Add("repo_id", typeof(short));
        table.Columns.Add("issue_number", typeof(int));
        table.Columns.Add("title_redacted", typeof(string));
        table.Columns.Add("body_redacted", typeof(string));
        table.Columns.Add("body_length", typeof(int));
        table.Columns.Add("body_truncated", typeof(bool));
        table.Columns.Add("content_hash", typeof(byte[]));
        table.Columns.Add("redaction_count", typeof(short));
        table.Columns.Add("state", typeof(string));
        table.Columns.Add("state_reason", typeof(string));
        table.Columns.Add("labels", typeof(string));
        table.Columns.Add("created_at", typeof(DateTime));
        table.Columns.Add("updated_at", typeof(DateTime));
        table.Columns.Add("closed_at", typeof(DateTime));
        table.Columns.Add("html_url", typeof(string));

        foreach (var issue in issues)
        {
            table.Rows.Add(
                issue.RepoId,
                issue.IssueNumber,
                issue.TitleRedacted,
                (object?)issue.BodyRedacted ?? DBNull.Value,
                issue.BodyLength,
                issue.BodyTruncated,
                issue.ContentHash,
                issue.RedactionCount,
                issue.State,
                (object?)issue.StateReason ?? DBNull.Value,
                issue.LabelsJson,
                issue.CreatedAt,
                issue.UpdatedAt,
                (object?)issue.ClosedAt ?? DBNull.Value,
                issue.HtmlUrl);
        }

        return table;
    }
}
