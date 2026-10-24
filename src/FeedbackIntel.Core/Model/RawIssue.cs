using System.Text.Json;

namespace FeedbackIntel.Core.Model;

/// <summary>
/// An issue exactly as fetched from GitHub, before redaction. Mirrors <c>raw.issue</c>.
/// It contains unredacted text, so it is never logged, saved to history, or published.
/// The author's username is deliberately not part of this type (FR-PRV-02).
/// </summary>
public sealed record RawIssue(
    short RepoId,
    int IssueNumber,
    string Title,
    string? Body,
    string State,
    string? StateReason,
    IReadOnlyList<string> Labels,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    DateTime? ClosedAt,
    string HtmlUrl,
    DateTime FetchedAt)
{
    /// <summary>The label names as a JSON list, the form stored in <c>raw.issue.labels</c>.</summary>
    public string LabelsJson => JsonSerializer.Serialize(Labels);
}
