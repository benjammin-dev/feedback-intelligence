using Dapper;

using FeedbackIntel.Core.Orchestration;

using Microsoft.Data.SqlClient;

namespace FeedbackIntel.Infrastructure.Sql;

/// <summary>Saves run records to <c>ops.run</c>, inserting the first time and updating after.</summary>
public sealed class SqlRunStore(DatabaseSettings settings) : IRunStore
{
    private const string UpsertSql = """
        MERGE ops.run WITH (HOLDLOCK) AS target
        USING (SELECT @RunId AS run_id) AS source
            ON target.run_id = source.run_id
        WHEN MATCHED THEN UPDATE SET
            finished_at = @FinishedAt,
            status = @Status,
            issues_fetched = @IssuesFetched,
            issues_rejected = @IssuesRejected,
            issues_redacted = @IssuesRedacted,
            ai_requests = @AiRequests,
            ai_cache_hits = @AiCacheHits,
            issues_unclassified = @IssuesUnclassified,
            failed_step = @FailedStep,
            error_message = @ErrorMessage
        WHEN NOT MATCHED THEN INSERT
            (run_id, [trigger], started_at, finished_at, status, issues_fetched, issues_rejected,
             issues_redacted, ai_requests, ai_cache_hits, issues_unclassified, failed_step, error_message)
        VALUES
            (@RunId, @Trigger, @StartedAt, @FinishedAt, @Status, @IssuesFetched, @IssuesRejected,
             @IssuesRedacted, @AiRequests, @AiCacheHits, @IssuesUnclassified, @FailedStep, @ErrorMessage);
        """;

    /// <inheritdoc />
    public async Task SaveAsync(RunRecord run, CancellationToken cancellationToken)
    {
        var parameters = new
        {
            run.RunId,
            run.Trigger,
            run.StartedAt,
            run.FinishedAt,
            Status = ToDatabaseValue(run.Status),
            run.IssuesFetched,
            run.IssuesRejected,
            run.IssuesRedacted,
            run.AiRequests,
            run.AiCacheHits,
            run.IssuesUnclassified,
            run.FailedStep,
            run.ErrorMessage,
        };

        await using var connection = new SqlConnection(settings.ConnectionString);
        await connection.ExecuteAsync(
            new CommandDefinition(UpsertSql, parameters, cancellationToken: cancellationToken));
    }

    /// <summary>The lowercase values allowed by the <c>CK_run_status</c> constraint.</summary>
    internal static string ToDatabaseValue(RunStatus status) => status switch
    {
        RunStatus.Running => "running",
        RunStatus.Succeeded => "succeeded",
        RunStatus.Failed => "failed",
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, null),
    };
}
