using System.Reflection;

using Dapper;

using FeedbackIntel.Core.Configuration;
using FeedbackIntel.Core.Model;
using FeedbackIntel.Core.Orchestration;
using FeedbackIntel.Core.Privacy;
using FeedbackIntel.Core.Reporting;
using FeedbackIntel.Infrastructure.History;
using FeedbackIntel.Infrastructure.ParquetFiles;
using FeedbackIntel.Infrastructure.Publishing;
using FeedbackIntel.Infrastructure.Sql;
using FeedbackIntel.Integration.Tests.Sql;

using Microsoft.Data.SqlClient;

namespace FeedbackIntel.Integration.Tests.Privacy;

/// <summary>
/// The end-to-end PII test (FR-PRV-04, NFR-PRIV-01): issues made of the known PII test set go
/// through the real redaction service, cleaned layer, reporting rebuild, publisher, and
/// history, and then every value in every output file is searched for the known values. CI
/// runs it on every pull request, so nothing that leaks can be deployed.
/// </summary>
public sealed class EndToEndPiiTests : IDisposable
{
    private static readonly DateTime Now = new(2026, 11, 1, 13, 17, 0, DateTimeKind.Utc);

    /// <summary>
    /// How to read the text in each Parquet file the pipeline writes. A new file must be added
    /// here, or <see cref="Pipeline_KnownPii_AppearsInNoOutputFile"/> fails, so no file escapes the scan.
    /// </summary>
    private static readonly Dictionary<string, Func<string, Task<IEnumerable<string>>>> ParquetReaders = new()
    {
        ["site/issues.parquet"] = TextOfAsync<IssueReportRow>,
        ["site/weekly_metrics.parquet"] = TextOfAsync<WeeklyMetricsRow>,
        ["site/run_health.parquet"] = TextOfAsync<RunHealthRow>,
        ["history/repositories.parquet"] = TextOfAsync<RepositoryRow>,
        ["history/issues.parquet"] = TextOfAsync<IssueRow>,
        ["history/runs.parquet"] = TextOfAsync<RunRow>,
        ["history/watermarks.parquet"] = TextOfAsync<WatermarkRow>,
    };

    private readonly string _output = Path.Combine(Path.GetTempPath(), $"fip-pii-{Guid.NewGuid():N}");

    public void Dispose()
    {
        if (Directory.Exists(_output))
        {
            Directory.Delete(_output, recursive: true);
        }
    }

    [Fact]
    public async Task Pipeline_KnownPii_AppearsInNoOutputFile()
    {
        await RunPipelineAsync();

        var values = await ReadEveryOutputValueAsync();

        Assert.NotEmpty(values);
        foreach (var sample in PiiTestSet.Samples)
        {
            var leaks = values.Where(value => value.Text.Contains(sample.Secret, StringComparison.Ordinal))
                .Select(value => value.File)
                .Distinct()
                .ToList();
            if (leaks.Count > 0)
            {
                Assert.Fail($"The {sample.Placeholder} sample leaked into: {string.Join(", ", leaks)}");
            }
        }
    }

    [Fact]
    public async Task Pipeline_KnownPii_IsPublishedAsPlaceholders()
    {
        var settings = await RunPipelineAsync();

        await using var connection = new SqlConnection(settings.ConnectionString);
        var titles = (await connection.QueryAsync<string>("SELECT title FROM reporting.issue")).ToList();
        Assert.Equal(PiiTestSet.Samples.Count, titles.Count);
        Assert.All(PiiTestSet.Samples, sample => Assert.Contains(titles, title => title.Contains(sample.Placeholder, StringComparison.Ordinal)));

        var site = await ReadEveryOutputValueAsync();
        Assert.Contains(site, value => value.File == "site/issues.parquet" && value.Text.Contains("<EMAIL_ADDRESS>", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Pipeline_KnownPii_IsAbsentFromTheCleanedLayer()
    {
        var settings = await RunPipelineAsync();

        await using var connection = new SqlConnection(settings.ConnectionString);
        var cleaned = (await connection.QueryAsync<(string Title, string? Body)>(
            "SELECT title_redacted, body_redacted FROM cleaned.issue")).ToList();
        Assert.Equal(PiiTestSet.Samples.Count, cleaned.Count);
        foreach (var sample in PiiTestSet.Samples)
        {
            Assert.DoesNotContain(cleaned, row => row.Title.Contains(sample.Secret, StringComparison.Ordinal)
                || (row.Body ?? "").Contains(sample.Secret, StringComparison.Ordinal));
        }
    }

    /// <summary>
    /// Stores one issue per sample, with the sample in its title and body, then runs the real
    /// steps from redaction to saving history.
    /// </summary>
    private async Task<DatabaseSettings> RunPipelineAsync()
    {
        var settings = await SqlServerTestDatabase.CreateMigratedAsync();
        var publishing = new PublishingOptions
        {
            OutputDirectory = Path.Combine(_output, "site"),
            HistoryDirectory = Path.Combine(_output, "history"),
        };

        var rawStore = new SqlIssueStore(settings);
        var repository = await rawStore.EnsureRepositoryAsync("microsoft", "winget-cli", Now.AddDays(-1), CancellationToken.None);
        await rawStore.InsertRawIssuesAsync(
            PiiTestSet.Samples.Select((sample, index) => new RawIssue(
                RepoId: repository.Id,
                IssueNumber: index + 1,
                Title: sample.Text,
                Body: $"Steps to reproduce:\n{sample.Text}\nThanks!",
                State: "open",
                StateReason: null,
                Labels: ["Issue-Bug"],
                CreatedAt: Now.AddDays(-2),
                UpdatedAt: Now.AddDays(-1),
                ClosedAt: null,
                HtmlUrl: $"https://github.com/microsoft/winget-cli/issues/{index + 1}",
                FetchedAt: Now)).ToList(),
            CancellationToken.None);

        var context = new RunContext(new RunRecord { RunId = 501, Trigger = "manual", StartedAt = Now });
        var redactor = await PresidioTestServices.CreateRedactorAsync();

        await new PrivacyCheckStep(redactor).ExecuteAsync(context, CancellationToken.None);
        await new RedactIssuesStep(rawStore, redactor, new SqlCleanedIssueStore(settings), new TextOptions())
            .ExecuteAsync(context, CancellationToken.None);
        await new RebuildReportingStep(new SqlReportingBuilder(settings, new IngestionOptions()))
            .ExecuteAsync(context, CancellationToken.None);
        await new SitePublisher(settings, publishing).PublishAsync(CancellationToken.None);
        await new SqlRunStore(settings).SaveAsync(context.Run.AsSucceeded(Now.AddMinutes(5)), CancellationToken.None);
        await new ParquetHistoryStore(settings, publishing).SaveAsync(CancellationToken.None);

        return settings;
    }

    /// <summary>Every text value in every output file, with the file it came from.</summary>
    private async Task<List<(string File, string Text)>> ReadEveryOutputValueAsync()
    {
        var values = new List<(string File, string Text)>();
        foreach (var path in Directory.EnumerateFiles(_output, "*", SearchOption.AllDirectories))
        {
            var name = Path.GetRelativePath(_output, path).Replace('\\', '/').Replace("/data/", "/", StringComparison.Ordinal);
            if (path.EndsWith(".parquet", StringComparison.Ordinal))
            {
                if (!ParquetReaders.TryGetValue(name, out var read))
                {
                    throw new InvalidOperationException($"{name} isn't in the PII scan; add it to ParquetReaders.");
                }

                values.AddRange((await read(path)).Select(text => (name, text)));
            }
            else
            {
                values.Add((name, await File.ReadAllTextAsync(path)));
            }
        }

        return values;
    }

    /// <summary>Reads a Parquet file with its row class and returns every string value in it.</summary>
    private static async Task<IEnumerable<string>> TextOfAsync<TRow>(string path)
        where TRow : class, new()
    {
        var textProperties = typeof(TRow).GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(property => property.PropertyType == typeof(string))
            .ToList();
        var rows = await ParquetTable<TRow>.ReadAsync(path, CancellationToken.None);
        return rows.SelectMany(row => textProperties.Select(property => (string?)property.GetValue(row) ?? "")).ToList();
    }
}
