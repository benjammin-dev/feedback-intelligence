using Dapper;

using FeedbackIntel.Core.Ingestion;

using Microsoft.Data.SqlClient;

namespace FeedbackIntel.Infrastructure.Sql;

/// <summary>Reads and advances watermarks in <c>ops.watermark</c>.</summary>
public sealed class SqlWatermarkStore(DatabaseSettings settings) : IWatermarkStore
{
    // The newest update time per repository comes from raw.issue, which holds exactly this
    // run's fetch. The GREATEST guard means a watermark can never move backward.
    private const string AdvanceSql = """
        MERGE ops.watermark WITH (HOLDLOCK) AS target
        USING
        (
            SELECT repo_id, MAX(updated_at) AS last_updated_at
            FROM raw.issue
            GROUP BY repo_id
        ) AS source
            ON target.repo_id = source.repo_id
        WHEN MATCHED AND source.last_updated_at > target.last_updated_at THEN
            UPDATE SET
                last_updated_at = source.last_updated_at,
                last_success_run_id = @runId
        WHEN NOT MATCHED BY TARGET THEN
            INSERT (repo_id, last_updated_at, last_success_run_id)
            VALUES (source.repo_id, source.last_updated_at, @runId);
        """;

    /// <inheritdoc />
    public async Task<IReadOnlyDictionary<short, DateTime>> GetAllAsync(CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(settings.ConnectionString);
        var rows = await connection.QueryAsync<(short RepoId, DateTime LastUpdatedAt)>(new CommandDefinition(
            "SELECT repo_id, last_updated_at FROM ops.watermark", cancellationToken: cancellationToken));

        return rows.ToDictionary(row => row.RepoId, row => DateTime.SpecifyKind(row.LastUpdatedAt, DateTimeKind.Utc));
    }

    /// <inheritdoc />
    public async Task AdvanceAsync(long runId, CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(settings.ConnectionString);
        await connection.ExecuteAsync(new CommandDefinition(AdvanceSql, new { runId }, cancellationToken: cancellationToken));
    }
}
