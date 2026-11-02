using System.Text;

using FeedbackIntel.Core.Configuration;

using Microsoft.Extensions.Configuration;

namespace FeedbackIntel.Core.Tests.Configuration;

/// <summary>
/// Validates the real <c>config/pipeline.json</c> in CI (FR-CFG-02), so a bad config change
/// fails its pull request instead of the next scheduled run. The file is read the same way the
/// pipeline reads it, with one stricter rule: a setting the code doesn't know, such as a
/// misspelled name, is an error rather than silently ignored.
/// </summary>
public class ConfigFileTests
{
    [Fact]
    public void RepositoryConfigFile_IsValid()
    {
        using var file = File.OpenRead(FindConfigFile());

        var settings = Load(file);

        Assert.Empty(settings.FindProblems());
        Assert.Contains(settings.Repositories, repository => repository.Enabled);
    }

    [Fact]
    public void MisspelledSetting_IsRejected()
    {
        var error = Assert.Throws<InvalidOperationException>(
            () => Load(Json("""{ "Ingestion": { "InitalHistoryDays": 30 } }""")));

        Assert.Contains("InitalHistoryDays", error.Message);
    }

    [Fact]
    public void WrongValueType_IsRejected()
    {
        Assert.Throws<InvalidOperationException>(
            () => Load(Json("""{ "Ingestion": { "InitialHistoryDays": "ninety" } }""")));
    }

    [Fact]
    public void ValueOutOfRange_IsReported()
    {
        var settings = Load(Json(
            """{ "Repositories": [ { "Owner": "microsoft", "Name": "winget-cli" } ], "Ingestion": { "InitialHistoryDays": 0 } }"""));

        Assert.Contains(settings.FindProblems(), problem => problem.StartsWith("Ingestion:InitialHistoryDays", StringComparison.Ordinal));
    }

    private static PipelineSettings Load(Stream json) =>
        new ConfigurationBuilder().AddJsonStream(json).Build()
            .Get<PipelineSettings>(options => options.ErrorOnUnknownConfiguration = true)
        ?? new PipelineSettings();

    private static MemoryStream Json(string text) => new(Encoding.UTF8.GetBytes(text));

    /// <summary>Walks up from the test output folder to the repository's config file.</summary>
    private static string FindConfigFile()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            var path = Path.Combine(directory.FullName, "config", "pipeline.json");
            if (File.Exists(path))
            {
                return path;
            }
        }

        throw new FileNotFoundException("config/pipeline.json wasn't found above the test folder.");
    }
}
