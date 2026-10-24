using FeedbackIntel.Core;
using FeedbackIntel.Core.Ingestion;
using FeedbackIntel.Core.Model;

using Octokit;

namespace FeedbackIntel.Infrastructure.GitHub;

/// <summary>Fetches issues from the GitHub REST API using Octokit.</summary>
public sealed class OctokitIssueSource(IGitHubClient client, IClock clock) : IIssueSource
{
    private static readonly ApiOptions AllPages = new() { PageSize = 100 };

    /// <inheritdoc />
    /// <remarks>Octokit doesn't accept a cancellation token here, so a fetch in progress finishes.</remarks>
    public async Task<IReadOnlyList<RawIssue>> FetchUpdatedSinceAsync(
        TrackedRepository repository, DateTime since, CancellationToken cancellationToken)
    {
        var request = new RepositoryIssueRequest
        {
            State = ItemStateFilter.All,
            Since = new DateTimeOffset(DateTime.SpecifyKind(since, DateTimeKind.Utc)),
            SortProperty = IssueSort.Updated,
            SortDirection = SortDirection.Ascending,
        };

        var issues = await client.Issue.GetAllForRepository(repository.Owner, repository.Name, request, AllPages);
        var fetchedAt = clock.UtcNow;

        // GitHub's issues endpoint also returns pull requests (FR-ING-03).
        return issues
            .Where(issue => issue.PullRequest is null)
            .Select(issue => ToRawIssue(issue, repository.Id, fetchedAt))
            .ToList();
    }

    /// <summary>
    /// Copies only the fields the pipeline uses. <c>issue.User</c> and other people fields
    /// are never read, so usernames can't reach the database (FR-PRV-02).
    /// </summary>
    private static RawIssue ToRawIssue(Issue issue, short repoId, DateTime fetchedAt) => new(
        RepoId: repoId,
        IssueNumber: issue.Number,
        Title: issue.Title ?? "",
        Body: issue.Body,
        State: issue.State.StringValue,
        StateReason: issue.StateReason?.StringValue,
        Labels: issue.Labels?.Select(label => label.Name).ToList() ?? [],
        CreatedAt: issue.CreatedAt.UtcDateTime,
        UpdatedAt: (issue.UpdatedAt ?? issue.CreatedAt).UtcDateTime,
        ClosedAt: issue.ClosedAt?.UtcDateTime,
        HtmlUrl: issue.HtmlUrl ?? "",
        FetchedAt: fetchedAt);
}
