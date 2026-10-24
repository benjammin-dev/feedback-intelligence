using FeedbackIntel.Core.Configuration;

namespace FeedbackIntel.Core.Tests.Configuration;

public class PipelineSettingsTests
{
    private static PipelineSettings Valid() => new()
    {
        Repositories = [new RepositoryOptions { Owner = "microsoft", Name = "winget-cli", Enabled = true }],
    };

    [Fact]
    public void FindProblems_ValidSettings_ReturnsNone()
    {
        Assert.Empty(Valid().FindProblems());
    }

    [Fact]
    public void FindProblems_NoEnabledRepository_IsReported()
    {
        var settings = Valid();
        settings.Repositories[0].Enabled = false;

        Assert.Contains(settings.FindProblems(), problem => problem.StartsWith("Repositories", StringComparison.Ordinal));
    }

    [Fact]
    public void FindProblems_MissingRepositoryName_IsReported()
    {
        var settings = Valid();
        settings.Repositories.Add(new RepositoryOptions { Owner = "microsoft", Name = "" });

        Assert.Single(settings.FindProblems());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(366)]
    public void FindProblems_HistoryDaysOutOfRange_IsReported(int days)
    {
        var settings = Valid();
        settings.Ingestion.InitialHistoryDays = days;

        Assert.Contains(settings.FindProblems(), problem => problem.Contains("InitialHistoryDays", StringComparison.Ordinal));
    }

    [Fact]
    public void FindProblems_SeveralProblems_ReportsAll()
    {
        var settings = Valid();
        settings.Ingestion.MaxRejectedPercent = 150;
        settings.Operations.SqlStartupWaitSeconds = 0;
        settings.Publishing.OutputDirectory = " ";

        Assert.Equal(3, settings.FindProblems().Count);
    }
}
