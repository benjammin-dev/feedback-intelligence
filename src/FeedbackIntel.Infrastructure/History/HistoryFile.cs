using System.Data;

using Dapper;

using FeedbackIntel.Infrastructure.ParquetFiles;

using Microsoft.Data.SqlClient;

namespace FeedbackIntel.Infrastructure.History;

/// <summary>One history file and the database table it saves and restores.</summary>
internal interface IHistoryFile
{
    /// <summary>The file name, for example <c>issues.parquet</c>.</summary>
    string FileName { get; }

    /// <summary>The database table, for example <c>cleaned.issue</c>.</summary>
    string Table { get; }

    /// <summary>The columns the file contains. The data contract test compares these.</summary>
    IReadOnlyList<string> ColumnNames { get; }

    /// <summary>Writes the table's rows to the file. An empty table writes no file.</summary>
    Task SaveAsync(SqlConnection connection, string path, CancellationToken cancellationToken);

    /// <summary>Loads the file's rows into the table.</summary>
    Task RestoreAsync(SqlConnection connection, string path, CancellationToken cancellationToken);
}

/// <summary>Saves and restores one table using the row class <typeparamref name="TRow"/>.</summary>
internal sealed class HistoryFile<TRow>(string fileName, string table, bool keepIdentity = false) : IHistoryFile
    where TRow : class, new()
{
    /// <inheritdoc />
    public string FileName => fileName;

    /// <inheritdoc />
    public string Table => table;

    /// <inheritdoc />
    public IReadOnlyList<string> ColumnNames => ParquetTable<TRow>.ColumnNames;

    /// <inheritdoc />
    public Task SaveAsync(SqlConnection connection, string path, CancellationToken cancellationToken) =>
        ParquetTable<TRow>.WriteAsync(connection, table, orderBy: null, path, cancellationToken);

    /// <inheritdoc />
    public async Task RestoreAsync(SqlConnection connection, string path, CancellationToken cancellationToken)
    {
        using var rows = ParquetTable<TRow>.ToDataTable(await ParquetTable<TRow>.ReadAsync(path, cancellationToken));
        var bulkOptions = SqlBulkCopyOptions.CheckConstraints | (keepIdentity ? SqlBulkCopyOptions.KeepIdentity : SqlBulkCopyOptions.Default);
        using (var bulkCopy = new SqlBulkCopy(connection, bulkOptions, null) { DestinationTableName = table })
        {
            foreach (DataColumn column in rows.Columns)
            {
                bulkCopy.ColumnMappings.Add(column.ColumnName, column.ColumnName);
            }

            await bulkCopy.WriteToServerAsync(rows, cancellationToken);
        }

        if (keepIdentity)
        {
            // Make sure new rows get IDs after the restored ones, never a duplicate.
            await connection.ExecuteAsync(new CommandDefinition(
                $"DBCC CHECKIDENT ('{table}', RESEED) WITH NO_INFOMSGS;", cancellationToken: cancellationToken));
        }
    }
}
