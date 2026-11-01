using System.Data.Common;
using System.Text.Json;

using FeedbackIntel.Core.Configuration;
using FeedbackIntel.Core.Publishing;
using FeedbackIntel.Infrastructure.ParquetFiles;
using FeedbackIntel.Infrastructure.Sql;

using Microsoft.Data.SqlClient;

namespace FeedbackIntel.Infrastructure.Publishing;

/// <summary>
/// Writes the dashboard's data files to <c>{OutputDirectory}/data</c>: two small JSON files
/// the Overview page loads first, and Parquet files for the other pages. Column names match
/// the database and the data contract exactly.
/// </summary>
public sealed class SitePublisher(DatabaseSettings settings, PublishingOptions options) : IPublisher
{
    /// <summary>
    /// The JSON files. Each reads only from the reporting schema and lists its columns
    /// explicitly, so a new column is never published by accident.
    /// </summary>
    internal static readonly IReadOnlyList<PublishedFile> JsonFiles =
    [
        new(
            "site_info.json",
            """
            SELECT data_updated_at, last_run_id, repositories, ai_model, prompt_version,
                   ai_daily_limit, ai_requests_today
            FROM reporting.site_info
            """,
            SingleRow: true),
        new(
            "overview.json",
            """
            SELECT repository, period, period_start, period_end, new_issues, closed_issues,
                   bug_count, critical_count, median_days_to_close
            FROM reporting.overview
            ORDER BY repository, period
            """,
            SingleRow: false),
    ];

    /// <summary>
    /// The Parquet files, read by the dashboard with DuckDB-WASM (ADR 0008). Each one's
    /// columns are exactly its row class's properties.
    /// </summary>
    internal static readonly IReadOnlyList<IPublishedParquetFile> ParquetFiles =
    [
        new PublishedParquetFile<IssueReportRow>("issues.parquet", "reporting.issue", "repository, issue_number"),
        new PublishedParquetFile<WeeklyMetricsRow>("weekly_metrics.parquet", "reporting.weekly_metrics", "repository, week_start"),
        new PublishedParquetFile<RunHealthRow>("run_health.parquet", "reporting.run_health", "started_at DESC"),
    ];

    /// <inheritdoc />
    public async Task PublishAsync(CancellationToken cancellationToken)
    {
        var dataDirectory = Path.Combine(options.OutputDirectory, "data");
        Directory.CreateDirectory(dataDirectory);

        await using var connection = new SqlConnection(settings.ConnectionString);
        await connection.OpenAsync(cancellationToken);

        foreach (var file in JsonFiles)
        {
            await using var command = new SqlCommand(file.Query, connection);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            await using var stream = File.Create(Path.Combine(dataDirectory, file.Name));
            await using var writer = new Utf8JsonWriter(stream);

            if (file.SingleRow)
            {
                if (!await reader.ReadAsync(cancellationToken))
                {
                    throw new InvalidOperationException($"No row found for {file.Name}.");
                }

                WriteRow(writer, reader);
            }
            else
            {
                writer.WriteStartArray();
                while (await reader.ReadAsync(cancellationToken))
                {
                    WriteRow(writer, reader);
                }

                writer.WriteEndArray();
            }

            await writer.FlushAsync(cancellationToken);
        }

        foreach (var file in ParquetFiles)
        {
            await file.WriteAsync(connection, dataDirectory, cancellationToken);
        }
    }

    private static void WriteRow(Utf8JsonWriter writer, DbDataReader reader)
    {
        writer.WriteStartObject();
        for (var i = 0; i < reader.FieldCount; i++)
        {
            writer.WritePropertyName(reader.GetName(i));
            WriteValue(writer, reader.GetValue(i));
        }

        writer.WriteEndObject();
    }

    private static void WriteValue(Utf8JsonWriter writer, object value)
    {
        switch (value)
        {
            case DBNull:
                writer.WriteNullValue();
                break;
            case string text:
                writer.WriteStringValue(text);
                break;
            case short or int or long:
                writer.WriteNumberValue(Convert.ToInt64(value));
                break;
            case decimal number:
                writer.WriteNumberValue(number);
                break;
            case bool flag:
                writer.WriteBooleanValue(flag);
                break;
            case DateTime time:
                // The database stores UTC, so mark it as UTC; the dashboard converts to local time.
                writer.WriteStringValue(DateTime.SpecifyKind(time, DateTimeKind.Utc));
                break;
            default:
                throw new NotSupportedException($"Can't publish values of type {value.GetType().Name}.");
        }
    }

    /// <summary>One published file and the query that fills it.</summary>
    internal sealed record PublishedFile(string Name, string Query, bool SingleRow);
}

/// <summary>One published Parquet file.</summary>
internal interface IPublishedParquetFile
{
    /// <summary>The file name, for example <c>issues.parquet</c>.</summary>
    string FileName { get; }

    /// <summary>The columns the file contains. The data contract test compares these.</summary>
    IReadOnlyList<string> ColumnNames { get; }

    /// <summary>Writes the file into <paramref name="directory"/>.</summary>
    Task WriteAsync(SqlConnection connection, string directory, CancellationToken cancellationToken);
}

/// <summary>Publishes one reporting table using the row class <typeparamref name="TRow"/>.</summary>
internal sealed class PublishedParquetFile<TRow>(string fileName, string table, string orderBy) : IPublishedParquetFile
    where TRow : class, new()
{
    /// <inheritdoc />
    public string FileName => fileName;

    /// <inheritdoc />
    public IReadOnlyList<string> ColumnNames => ParquetTable<TRow>.ColumnNames;

    /// <inheritdoc />
    public Task WriteAsync(SqlConnection connection, string directory, CancellationToken cancellationToken) =>
        ParquetTable<TRow>.WriteAsync(connection, table, orderBy, Path.Combine(directory, fileName), cancellationToken);
}
