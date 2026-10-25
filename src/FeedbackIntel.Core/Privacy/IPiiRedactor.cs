namespace FeedbackIntel.Core.Privacy;

/// <summary>
/// Removes personal information from text before it is stored in the cleaned layer,
/// sent to AI, or published (FR-PRV-01). Behind an interface so the detection service
/// can be replaced without changing the pipeline (ADR 0007).
/// </summary>
public interface IPiiRedactor
{
    /// <summary>
    /// Returns the text with each piece of personal information replaced by a typed
    /// placeholder such as <c>&lt;EMAIL_ADDRESS&gt;</c>, and how many were replaced.
    /// </summary>
    Task<RedactedText> RedactAsync(string text, CancellationToken cancellationToken);
}

/// <summary>Text after redaction.</summary>
/// <param name="Text">The text with personal information replaced by placeholders.</param>
/// <param name="RedactionCount">How many items were replaced.</param>
public sealed record RedactedText(string Text, int RedactionCount);
