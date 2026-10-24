namespace FeedbackIntel.Core.Model;

/// <summary>A GitHub repository the pipeline collects issues from, as stored in <c>cleaned.repository</c>.</summary>
/// <param name="Id">The short numeric ID other tables use.</param>
/// <param name="Owner">The GitHub account that owns the repository, for example <c>microsoft</c>.</param>
/// <param name="Name">The repository name, for example <c>winget-cli</c>.</param>
public sealed record TrackedRepository(short Id, string Owner, string Name)
{
    /// <summary>The full name, for example <c>microsoft/winget-cli</c>.</summary>
    public string FullName => $"{Owner}/{Name}";
}
