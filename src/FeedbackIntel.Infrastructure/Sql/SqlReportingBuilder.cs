using System.Data;

using Dapper;

using FeedbackIntel.Core.Configuration;
using FeedbackIntel.Core.Reporting;

using Microsoft.Data.SqlClient;

namespace FeedbackIntel.Infrastructure.Sql;

/// <summary>
/// Runs the <c>reporting.rebuild</c> stored procedure. The logic lives in T-SQL
/// (latest version in migration 0005), next to the data it works on.
/// </summary>
public sealed class SqlReportingBuilder(DatabaseSettings settings, IngestionOptions ingestion) : IReportingBuilder
{
    /// <inheritdoc />
    public async Task RebuildAsync(DateTime asOf, long runId, CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(settings.ConnectionString);
        await connection.ExecuteAsync(new CommandDefinition(
            "reporting.rebuild",
            new { as_of = asOf, run_id = runId, initial_history_days = ingestion.InitialHistoryDays },
            commandType: CommandType.StoredProcedure,
            cancellationToken: cancellationToken));
    }
}
