namespace FeedbackIntel.Core.Configuration;

/// <summary>The <c>Text</c> section of <c>config/pipeline.json</c>.</summary>
public sealed class TextOptions
{
    /// <summary>
    /// How much of each redacted body is kept for the run. 4,000 is the largest fixed
    /// <c>nvarchar</c> size, and enough context for the AI step.
    /// </summary>
    public int BodyMaxCharacters { get; set; } = 4000;
}
