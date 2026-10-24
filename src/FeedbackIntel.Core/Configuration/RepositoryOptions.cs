namespace FeedbackIntel.Core.Configuration;

/// <summary>One entry in the <c>Repositories</c> list of <c>config/pipeline.json</c>.</summary>
public sealed class RepositoryOptions
{
    /// <summary>The GitHub account that owns the repository.</summary>
    public string Owner { get; set; } = "";

    /// <summary>The repository name.</summary>
    public string Name { get; set; } = "";

    /// <summary>Whether the pipeline currently collects this repository's issues.</summary>
    public bool Enabled { get; set; } = true;
}
