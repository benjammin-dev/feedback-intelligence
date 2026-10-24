using FeedbackIntel.Core.Model;

namespace FeedbackIntel.Core.Ingestion;

/// <summary>
/// Checks fetched issues before they are stored (FR-ING-05). A record that fails is
/// skipped and counted rather than stored, so one malformed issue can't stop the run.
/// </summary>
public static class RawIssueValidator
{
    /// <summary>Column sizes from the data dictionary.</summary>
    public const int MaxTitleLength = 1000;
    public const int MaxStateReasonLength = 20;
    public const int MaxLabelsLength = 2000;
    public const int MaxUrlLength = 300;

    /// <summary>
    /// Returns the reason the issue is invalid, or null if it's valid. Reasons describe
    /// the problem only, never the issue's text, so they are safe to log.
    /// </summary>
    public static string? FindProblem(RawIssue issue)
    {
        if (issue.IssueNumber <= 0)
        {
            return "issue number is not positive";
        }

        if (string.IsNullOrWhiteSpace(issue.Title))
        {
            return "title is empty";
        }

        if (issue.Title.Length > MaxTitleLength)
        {
            return $"title is longer than {MaxTitleLength} characters";
        }

        if (issue.State is not ("open" or "closed"))
        {
            return "state is not open or closed";
        }

        if (issue.State == "closed" && issue.ClosedAt is null)
        {
            return "closed issue has no closed date";
        }

        if (issue.StateReason?.Length > MaxStateReasonLength)
        {
            return $"state reason is longer than {MaxStateReasonLength} characters";
        }

        if (issue.UpdatedAt < issue.CreatedAt)
        {
            return "updated date is before created date";
        }

        if (issue.LabelsJson.Length > MaxLabelsLength)
        {
            return $"labels are longer than {MaxLabelsLength} characters";
        }

        if (!issue.HtmlUrl.StartsWith("https://github.com/", StringComparison.Ordinal)
            || issue.HtmlUrl.Length > MaxUrlLength)
        {
            return "link is not a valid GitHub URL";
        }

        return null;
    }
}
