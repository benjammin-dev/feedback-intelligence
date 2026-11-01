using System.Data;
using System.Reflection;
using System.Text.Json.Serialization;

using Dapper;

using Microsoft.Data.SqlClient;

using Parquet.Serialization;

namespace FeedbackIntel.Infrastructure.ParquetFiles;

/// <summary>
/// Moves rows between a SQL Server table and a Parquet file using the row class
/// <typeparamref name="TRow"/>. The columns are exactly the row class's properties, named by
/// their <c>JsonPropertyName</c>, so a file can never hold a column its class doesn't declare.
/// Used for both published files and history.
/// </summary>
internal static class ParquetTable<TRow>
    where TRow : class, new()
{
    private static readonly IReadOnlyList<(PropertyInfo Property, string Column)> Columns =
        typeof(TRow).GetProperties()
            .Select(property => (property, property.GetCustomAttribute<JsonPropertyNameAttribute>()!.Name))
            .ToList();

    /// <summary>The column names, in order. The data contract test compares these.</summary>
    public static IReadOnlyList<string> ColumnNames { get; } = Columns.Select(column => column.Column).ToList();

    /// <summary>
    /// Selects the row class's columns from <paramref name="table"/> and writes them to
    /// <paramref name="path"/>. Columns are listed by name, so nothing else in the table, such
    /// as <c>cleaned.issue.body_redacted</c>, can be read. An empty table writes no file.
    /// </summary>
    public static async Task WriteAsync(
        SqlConnection connection, string table, string? orderBy, string path, CancellationToken cancellationToken)
    {
        var selectList = string.Join(", ", Columns.Select(column => $"[{column.Column}] AS [{column.Property.Name}]"));
        var order = orderBy is null ? "" : $" ORDER BY {orderBy}";
        var rows = (await connection.QueryAsync<TRow>(new CommandDefinition(
            $"SELECT {selectList} FROM {table}{order}", cancellationToken: cancellationToken))).ToList();

        if (rows.Count == 0)
        {
            File.Delete(path);
            return;
        }

        await using var stream = File.Create(path);
        await ParquetSerializer.SerializeAsync(rows, stream, cancellationToken: cancellationToken);
    }

    /// <summary>Reads every row from a Parquet file.</summary>
    public static async Task<IList<TRow>> ReadAsync(string path, CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(path);
        var result = await ParquetSerializer.DeserializeAsync<TRow>(stream, cancellationToken: cancellationToken);
        return result.Data;
    }

    /// <summary>Converts rows to a table whose column names match the database, for SqlBulkCopy.</summary>
    public static DataTable ToDataTable(IEnumerable<TRow> rows)
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
