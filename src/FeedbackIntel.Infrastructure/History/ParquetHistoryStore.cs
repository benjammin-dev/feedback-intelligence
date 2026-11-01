using Dapper;

using FeedbackIntel.Core.Configuration;
using FeedbackIntel.Core.History;
using FeedbackIntel.Infrastructure.Sql;

using Microsoft.Data.SqlClient;

namespace FeedbackIntel.Infrastructure.History;

/// <summary>
/// Saves history as Parquet files and restores it into SQL Server (ADR 0005). The files
/// match the data contract's history list; the workflow keeps them on the <c>data</c> branch.
/// </summary>
public sealed class ParquetHistoryStore(DatabaseSettings settings, PublishingOptions options) : IHistoryStore
{
    /// <summary>Its presence means history exists. Every successful run writes it.</summary>
    internal const string MarkerFile = "repositories.parquet";

    /// <summary>
    /// Every history file, in restore order: repositories first, because the other tables
    /// refer to them. Enrichment and digests join this list in M4 and M5.
    /// </summary>
    internal static readonly IReadOnlyList<IHistoryFile> Files =
    [
        new HistoryFile<RepositoryRow>(MarkerFile, "cleaned.repository", keepIdentity: true),
        new HistoryFile<IssueRow>("issues.parquet", "cleaned.issue"),
        new HistoryFile<RunRow>("runs.parquet", "ops.run"),
        new HistoryFile<WatermarkRow>("watermarks.parquet", "ops.watermark"),
    ];

    /// <inheritdoc />
    public bool HistoryExists() => File.Exists(PathOf(MarkerFile));

    /// <inheritdoc />
    public async Task<bool> DatabaseHasDataAsync(CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(settings.ConnectionString);
        return await connection.ExecuteScalarAsync<bool>(new CommandDefinition(
            "SELECT CAST(CASE WHEN EXISTS (SELECT 1 FROM cleaned.repository) THEN 1 ELSE 0 END AS bit)",
            cancellationToken: cancellationToken));
    }

    /// <inheritdoc />
    /// <remarks>A file missing from older history (for example one added later) restores nothing.</remarks>
    public async Task RestoreAsync(CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(settings.ConnectionString);
        await connection.OpenAsync(cancellationToken);

        foreach (var file in Files.Where(file => File.Exists(PathOf(file.FileName))))
        {
            await file.RestoreAsync(connection, PathOf(file.FileName), cancellationToken);
        }
    }

    /// <inheritdoc />
    public async Task SaveAsync(CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(options.HistoryDirectory);

        await using var connection = new SqlConnection(settings.ConnectionString);
        await connection.OpenAsync(cancellationToken);

        foreach (var file in Files)
        {
            await file.SaveAsync(connection, PathOf(file.FileName), cancellationToken);
        }
    }

    private string PathOf(string fileName) => Path.Combine(options.HistoryDirectory, fileName);
}
