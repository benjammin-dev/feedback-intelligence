using System.Text.Json;

using Dapper;

using FeedbackIntel.Core.Configuration;
using FeedbackIntel.Core.Model;
using FeedbackIntel.Core.Orchestration;
using FeedbackIntel.Core.Text;
using FeedbackIntel.Infrastructure.History;
using FeedbackIntel.Infrastructure.Publishing;
using FeedbackIntel.Infrastructure.Sql;
using FeedbackIntel.Integration.Tests.Sql;

using Microsoft.Data.SqlClient;

using Parquet;

namespace FeedbackIntel.Integration.Tests.Contract;

/// <summary>
/// The data contract test (NFR-PRIV-02): everything the pipeline writes, to the public site
/// or the public data branch, must be listed in the contract with exactly the listed columns.
/// Adding a file or column therefore requires changing the data dictionary in the same pull request.
/// </summary>
public class DataContractTests : IDisposable
{
    private static readonly DateTime AsOf = new(2026, 10, 24, 6, 0, 0, DateTimeKind.Utc);

    private readonly DataContract _contract = DataContract.Load();
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"fip-contract-{Guid.NewGuid():N}");

    [Fact]
    public void Contract_IsReadFromTheDataDictionary()
    {
        Assert.Contains("issues.parquet", _contract.Published.Keys);
        Assert.Contains("issues.parquet", _contract.History.Keys);
        Assert.DoesNotContain("body_redacted", _contract.History["issues.parquet"]);
        Assert.Contains("title_redacted", _contract.History["issues.parquet"]);
    }

    [Fact]
    public void DeclaredPublishedParquetFiles_MatchTheContract()
    {
        foreach (var file in SitePublisher.ParquetFiles)
        {
            AssertMatches(_contract.Published, file.FileName, file.ColumnNames);
        }
    }

    [Fact]
    public void DeclaredHistoryFiles_MatchTheContract()
    {
        foreach (var file in ParquetHistoryStore.Files)
        {
            AssertMatches(_contract.History, file.FileName, file.ColumnNames);
        }
    }

    [Fact]
    public async Task EveryFileWritten_IsInTheContractWithExactlyItsColumns()
    {
        var (siteDirectory, historyDirectory) = await RunOutputsAsync();

        var published = Directory.GetFiles(siteDirectory);
        var history = Directory.GetFiles(historyDirectory);
        Assert.NotEmpty(published);
        Assert.NotEmpty(history);

        foreach (var path in published)
        {
            AssertMatches(_contract.Published, Path.GetFileName(path), await ColumnsOfAsync(path));
        }

        foreach (var path in history)
        {
            AssertMatches(_contract.History, Path.GetFileName(path), await ColumnsOfAsync(path));
        }
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }

        GC.SuppressFinalize(this);
    }

    private static void AssertMatches(
        IReadOnlyDictionary<string, IReadOnlySet<string>> contract, string fileName, IEnumerable<string> columns)
    {
        Assert.True(contract.ContainsKey(fileName), $"{fileName} is written but isn't in the data contract.");
        var actual = columns.ToHashSet();
        var allowed = contract[fileName];
        Assert.True(
            actual.SetEquals(allowed),
            $"{fileName} columns differ from the contract. Not allowed: [{string.Join(", ", actual.Except(allowed))}]. " +
            $"Missing: [{string.Join(", ", allowed.Except(actual))}].");
    }

    private static async Task<IReadOnlyList<string>> ColumnsOfAsync(string path)
    {
        if (path.EndsWith(".json", StringComparison.Ordinal))
        {
            using var json = JsonDocument.Parse(await File.ReadAllTextAsync(path));
            var first = json.RootElement.ValueKind == JsonValueKind.Array ? json.RootElement[0] : json.RootElement;
            return first.EnumerateObject().Select(property => property.Name).ToList();
        }

        await using var reader = await ParquetReader.CreateAsync(path);
        return reader.Schema.GetDataFields().Select(field => field.Name).ToList();
    }

    /// <summary>Seeds a database, then runs the real reporting, publishing, and history code.</summary>
    private async Task<(string Site, string History)> RunOutputsAsync()
    {
        var settings = await SqlServerTestDatabase.CreateMigratedAsync();
        var store = new SqlIssueStore(settings);
        var repository = await store.EnsureRepositoryAsync("microsoft", "winget-cli", AsOf, CancellationToken.None);

        await new SqlCleanedIssueStore(settings).UpsertAsync(
            [
                Issue(repository.Id, 1, created: AsOf.AddDays(-3), closed: null),
                Issue(repository.Id, 2, created: AsOf.AddDays(-20), closed: AsOf.AddDays(-10)),
            ],
            runId: 41,
            CancellationToken.None);
        await new SqlRunStore(settings).SaveAsync(
            new RunRecord { RunId = 41, Trigger = "schedule", StartedAt = AsOf.AddDays(-1) }.AsSucceeded(AsOf.AddDays(-1).AddMinutes(1)),
            CancellationToken.None);
        await using (var connection = new SqlConnection(settings.ConnectionString))
        {
            await connection.ExecuteAsync(
                "INSERT INTO ops.watermark (repo_id, last_updated_at, last_success_run_id) VALUES (@id, @at, 41)",
                new { id = repository.Id, at = AsOf.AddDays(-1) });
        }

        var options = new PublishingOptions
        {
            OutputDirectory = Path.Combine(_root, "site"),
            HistoryDirectory = Path.Combine(_root, "history"),
        };
        await new SqlReportingBuilder(settings, new IngestionOptions()).RebuildAsync(AsOf, 42, CancellationToken.None);
        await new SitePublisher(settings, options).PublishAsync(CancellationToken.None);
        await new ParquetHistoryStore(settings, options).SaveAsync(CancellationToken.None);

        return (Path.Combine(options.OutputDirectory, "data"), options.HistoryDirectory);
    }

    private static CleanedIssue Issue(short repoId, int number, DateTime created, DateTime? closed) => new(
        RepoId: repoId,
        IssueNumber: number,
        TitleRedacted: "Crash on <EMAIL_ADDRESS>",
        BodyRedacted: "Body text that must never leave the run",
        BodyLength: 39,
        BodyTruncated: false,
        ContentHash: ContentHasher.Compute("Crash on <EMAIL_ADDRESS>", "Body text that must never leave the run"),
        RedactionCount: 1,
        State: closed is null ? "open" : "closed",
        StateReason: closed is null ? null : "completed",
        LabelsJson: "[]",
        CreatedAt: created,
        UpdatedAt: closed ?? created,
        ClosedAt: closed,
        HtmlUrl: $"https://github.com/microsoft/winget-cli/issues/{number}");
}
