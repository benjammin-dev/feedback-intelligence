namespace FeedbackIntel.Core.Configuration;

/// <summary>The <c>Publishing</c> section of <c>config/pipeline.json</c>.</summary>
public sealed class PublishingOptions
{
    /// <summary>
    /// The folder the website is assembled in. Data files go in its <c>data</c> subfolder.
    /// Local runs can override this so they never touch the live site.
    /// </summary>
    public string OutputDirectory { get; set; } = "out/site";
}
