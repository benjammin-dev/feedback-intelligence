using System.Text.Json;

using FeedbackIntel.Infrastructure.GitHub;

using Octokit;

namespace FeedbackIntel.Integration.Tests.GitHub;

/// <summary>
/// Checks how GitHub's issue data is converted, without calling GitHub: pull requests are
/// dropped (FR-ING-03) and no information about people is kept (FR-PRV-02).
/// </summary>
public class OctokitIssueSourceTests
{
    private static readonly DateTimeOffset Created = new(2026, 10, 1, 9, 0, 0, TimeSpan.Zero);
    private static readonly DateTime FetchedAt = new(2026, 10, 24, 6, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void ToRawIssues_PullRequests_AreDropped()
    {
        var issues = new[] { GitHubIssue(1), GitHubIssue(2, pullRequest: new PullRequest(2)), GitHubIssue(3) };

        var result = OctokitIssueSource.ToRawIssues(issues, repoId: 1, FetchedAt);

        Assert.Equal<int>([1, 3], result.Select(issue => issue.IssueNumber));
    }

    [Fact]
    public void ToRawIssues_PeopleFields_AreNeverCopied()
    {
        var person = new User(
            avatarUrl: "https://avatars.example.com/u/1",
            bio: "Bio of a private person",
            blog: null,
            collaborators: 0,
            company: "Private Company",
            createdAt: Created,
            updatedAt: Created,
            diskUsage: 0,
            email: "private.person@example.com",
            followers: 0,
            following: 0,
            hireable: null,
            htmlUrl: "https://github.com/private-login",
            totalPrivateRepos: 0,
            id: 1,
            location: "Private City",
            login: "private-login",
            name: "Private Person",
            nodeId: "node",
            ownedPrivateRepos: 0,
            plan: null,
            privateGists: 0,
            publicGists: 0,
            publicRepos: 0,
            url: "https://api.github.com/users/private-login",
            permissions: null,
            siteAdmin: false,
            ldapDistinguishedName: null,
            suspendedAt: null);

        var result = OctokitIssueSource.ToRawIssues([GitHubIssue(1, user: person)], repoId: 1, FetchedAt);

        var everything = JsonSerializer.Serialize(Assert.Single(result));
        Assert.DoesNotContain("private", everything, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ToRawIssues_CopiesIssueFields()
    {
        var issue = GitHubIssue(
            4521,
            state: ItemState.Closed,
            stateReason: ItemStateReason.Completed,
            closedAt: Created.AddDays(2),
            labels: [new Label(1, "url", "Issue-Bug", "node", "ff0000", "", false)]);

        var raw = Assert.Single(OctokitIssueSource.ToRawIssues([issue], repoId: 7, FetchedAt));

        Assert.Equal(7, raw.RepoId);
        Assert.Equal(4521, raw.IssueNumber);
        Assert.Equal("winget upgrade fails", raw.Title);
        Assert.Equal("closed", raw.State);
        Assert.Equal("completed", raw.StateReason);
        Assert.Equal<string>(["Issue-Bug"], raw.Labels);
        Assert.Equal(Created.UtcDateTime, raw.CreatedAt);
        Assert.Equal(Created.AddDays(2).UtcDateTime, raw.ClosedAt);
        Assert.Equal("https://github.com/microsoft/winget-cli/issues/4521", raw.HtmlUrl);
        Assert.Equal(FetchedAt, raw.FetchedAt);
    }

    [Fact]
    public void ToRawIssues_MissingUpdateTime_UsesCreatedTime()
    {
        var raw = Assert.Single(OctokitIssueSource.ToRawIssues([GitHubIssue(1, hasUpdateTime: false)], repoId: 1, FetchedAt));

        Assert.Equal(Created.UtcDateTime, raw.UpdatedAt);
    }

    private static Issue GitHubIssue(
        int number,
        PullRequest? pullRequest = null,
        User? user = null,
        ItemState state = ItemState.Open,
        ItemStateReason? stateReason = null,
        DateTimeOffset? closedAt = null,
        IReadOnlyList<Label>? labels = null,
        bool hasUpdateTime = true) =>
        new(
            url: $"https://api.github.com/repos/microsoft/winget-cli/issues/{number}",
            htmlUrl: $"https://github.com/microsoft/winget-cli/issues/{number}",
            commentsUrl: "",
            eventsUrl: "",
            number: number,
            state: state,
            title: "winget upgrade fails",
            body: "Steps to reproduce",
            closedBy: user,
            user: user,
            labels: labels ?? [],
            assignee: user,
            assignees: user is null ? [] : [user],
            milestone: null,
            comments: 0,
            pullRequest: pullRequest,
            closedAt: closedAt,
            createdAt: Created,
            updatedAt: hasUpdateTime ? Created.AddHours(1) : null,
            id: number,
            nodeId: "node",
            locked: false,
            repository: null,
            reactions: null,
            activeLockReason: null,
            stateReason: stateReason);
}
