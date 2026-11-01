namespace FeedbackIntel.Core.Model;

/// <summary>
/// An issue with personal information removed. Mirrors <c>cleaned.issue</c>; see the data
/// dictionary for each column. Safe to store, show, and send to AI.
/// </summary>
public sealed record CleanedIssue(
    short RepoId,
    int IssueNumber,
    string TitleRedacted,
    string? BodyRedacted,
    int BodyLength,
    bool BodyTruncated,
    byte[] ContentHash,
    short RedactionCount,
    string State,
    string? StateReason,
    string LabelsJson,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    DateTime? ClosedAt,
    string HtmlUrl);
