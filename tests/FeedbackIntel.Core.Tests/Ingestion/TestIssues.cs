using FeedbackIntel.Core.Model;

namespace FeedbackIntel.Core.Tests.Ingestion;

/// <summary>Builds valid issues that tests can then change with <c>with</c> expressions.</summary>
internal static class TestIssues
{
    public static readonly DateTime Created = new(2026, 10, 1, 9, 0, 0, DateTimeKind.Utc);

    public static RawIssue Valid(int number = 1, short repoId = 1) => new(
        RepoId: repoId,
        IssueNumber: number,
        Title: "winget upgrade fails with 0x8a15000f",
        Body: "Steps to reproduce...",
        State: "open",
        StateReason: null,
        Labels: ["Issue-Bug"],
        CreatedAt: Created,
        UpdatedAt: Created.AddHours(2),
        ClosedAt: null,
        HtmlUrl: $"https://github.com/microsoft/winget-cli/issues/{number}",
        FetchedAt: Created.AddDays(1));
}
