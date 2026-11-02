using FeedbackIntel.Core.Orchestration;
using FeedbackIntel.Infrastructure.Actions;

using Microsoft.Extensions.Logging.Abstractions;

namespace FeedbackIntel.Integration.Tests.Actions;

public sealed class GitHubActionsOutputTests : IDisposable
{
    private static readonly DateTime Start = new(2026, 11, 1, 13, 17, 0, DateTimeKind.Utc);

    private readonly string _path = Path.Combine(Path.GetTempPath(), $"fip-output-{Guid.NewGuid():N}");

    public void Dispose() => File.Delete(_path);

    [Fact]
    public void WriteRunResult_FailedRun_WritesStepAndOneLineError()
    {
        var run = Run(RunStatus.Failed);
        run.FailedStep = "quality checks";
        run.ErrorMessage = "InvalidDataException: 1 quality check(s) failed:\nsee `table`";

        Output(_path).WriteRunResult(run);

        Assert.Equal(
            "failed_step=quality checks\nerror=InvalidDataException: 1 quality check(s) failed: see 'table'\n",
            File.ReadAllText(_path));
    }

    [Fact]
    public void WriteRunResult_SucceededRun_WritesNothing()
    {
        Output(_path).WriteRunResult(Run(RunStatus.Succeeded));

        Assert.False(File.Exists(_path));
    }

    [Fact]
    public void ReportStale_WritesWholeHours()
    {
        Output(_path).ReportStale(TimeSpan.FromHours(49.7));

        Assert.Equal("stale_hours=49\n", File.ReadAllText(_path));
    }

    [Fact]
    public void NoOutputFile_OutsideGitHubActions_DoesNothing()
    {
        var output = Output(null);

        output.ReportStale(TimeSpan.FromHours(50));
        output.WriteRunResult(Run(RunStatus.Failed));

        Assert.False(File.Exists(_path));
    }

    [Fact]
    public void ToSingleLine_LongText_IsShortened()
    {
        var line = GitHubActionsOutput.ToSingleLine(new string('x', 600), GitHubActionsOutput.MaxErrorLength);

        Assert.Equal(GitHubActionsOutput.MaxErrorLength, line.Length);
        Assert.EndsWith("...", line, StringComparison.Ordinal);
    }

    private static GitHubActionsOutput Output(string? path) =>
        new(path, NullLogger<GitHubActionsOutput>.Instance);

    private static RunRecord Run(RunStatus status) =>
        new() { RunId = 1, Trigger = "schedule", StartedAt = Start, Status = status };
}
