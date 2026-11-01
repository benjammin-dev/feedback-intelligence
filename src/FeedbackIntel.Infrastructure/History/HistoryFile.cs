using System.Data;
using System.Reflection;
using System.Text.Json.Serialization;

using Dapper;

using Microsoft.Data.SqlClient;

using Parquet.Serialization;

namespace FeedbackIntel.Infrastructure.History;

/// <summary>One history file and the database table it saves and restores.</summary>
internal interface IHistoryFile
{
    /// <summary>The file name, for example <c>issues.parquet</c>.</summary>
    string FileName { get; }

    /// <summary>The database table, for example <c>cleaned.issue</c>.</summary>
    string Table { get; }

    /// <summary>Writes the table's rows to the file. An empty table writes no file.</summary>
    Task SaveAsync(SqlConnection connection, string path, CancellationToken cancellationToken);

    /// <summary>Loads the file's rows into the table.</summary>
    Task RestoreAsync(SqlConnection connection, string path, CancellationToken cancellationToken);
}

/// <summary>
/// Saves and restores one table using the row class <typeparamref name="TRow"/>. The
/// columns are exactly the row class's properties, named by their <c>JsonPropertyName</c>.
/// </summary>
internal sealed class HistoryFile<TRow>(string fileName, string table, bool keepIdentity = false) : IHistoryFile
    where TRow : class, new()
{
    private static readonly IReadOnlyList<(PropertyInfo Property, string Column)> Columns =
        typeof(TRow).GetProperties()
            .Select(property => (property, property.GetCustomAttribute<JsonPropertyNameAttribute>()!.Name))
            .ToList();

    /// <inheritdoc />
    public string FileName => fileName;

    /// <inheritdoc />
    public string Table => table;

    /// <summary>The column names, in order. Used by the data contract test.</summary>
    public static IReadOnlyList<string> ColumnNames => Columns.Select(column => column.Column).ToList();

    /// <inheritdoc />
    public async Task SaveAsync(SqlConnection connection, string path, CancellationToken cancellationToken)
    {
        // Each column is selected explicitly and renamed to its property, so nothing outside
        // the row class (such as cleaned.issue.body_redacted) can be read, let alone saved.
        var selectList = string.Join(", ", Columns.Select(column => $"[{column.Column}] AS [{column.Property.Name}]"));
        var rows = (await connection.QueryAsync<TRow>(new CommandDefinition(
            $"SELECT {selectList} FROM {table}", cancellationToken: cancellationToken))).ToList();

        if (rows.Count == 0)
        {
            File.Delete(path);
            return;
        }

        await using var stream = File.Create(path);
        await ParquetSerializer.SerializeAsync(rows, stream, cancellationToken: cancellationToken);
    }

    /// <inheritdoc />
    public async Task RestoreAsync(SqlConnection connection, string path, CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(path);
        var result = await ParquetSerializer.DeserializeAsync<TRow>(stream, cancellationToken: cancellationToken);

        using var rows = ToDataTable(result.Data);
        var bulkOptions = SqlBulkCopyOptions.CheckConstraints | (keepIdentity ? SqlBulkCopyOptions.KeepIdentity : SqlBulkCopyOptions.Default);
        using var bulkCopy = new SqlBulkCopy(connection, bulkOptions, null) { DestinationTableName = table };
        foreach (DataColumn column in rows.Columns)
        {
            bulkCopy.ColumnMappings.Add(column.ColumnName, column.ColumnName);
        }

        await bulkCopy.WriteToServerAsync(rows, cancellationToken);

        if (keepIdentity)
        {
            // Make sure new rows get IDs after the restored ones, never a duplicate.
            await connection.ExecuteAsync(new CommandDefinition(
                $"DBCC CHECKIDENT ('{table}', RESEED) WITH NO_INFOMSGS;", cancellationToken: cancellationToken));
        }
    }

    private static DataTable ToDataTable(IEnumerable<TRow> rows)
    {
        var dataTable = new DataTable();
        foreach (var (property, column) in Columns)
        {
            dataTable.Columns.Add(column, Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType);
        }

        foreach (var row in rows)
        {
            dataTable.Rows.Add(Columns.Select(column => ToDatabaseValue(column.Property.GetValue(row))).ToArray());
        }

        return dataTable;
    }

    /// <summary>
    /// Timestamps are stored in UTC. If a reader hands back local time, convert it so a
    /// restore can never shift times by the machine's time zone.
    /// </summary>
    private static object ToDatabaseValue(object? value) => value switch
    {
        null => DBNull.Value,
        DateTime { Kind: DateTimeKind.Local } local => local.ToUniversalTime(),
        _ => value,
    };
}
