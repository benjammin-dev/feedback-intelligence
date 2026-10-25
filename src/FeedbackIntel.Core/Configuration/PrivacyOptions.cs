namespace FeedbackIntel.Core.Configuration;

/// <summary>The <c>Privacy</c> section of <c>config/pipeline.json</c>.</summary>
public sealed class PrivacyOptions
{
    /// <summary>Where the Presidio analyzer listens. It finds personal information.</summary>
    public string AnalyzerUrl { get; set; } = "http://localhost:5002";

    /// <summary>Where the Presidio anonymizer listens. It replaces what the analyzer found.</summary>
    public string AnonymizerUrl { get; set; } = "http://localhost:5001";

    /// <summary>
    /// The lowest confidence score that counts as a finding. Kept low on purpose: redacting
    /// harmless text is the safer mistake (ADR 0007). Presidio scores IP addresses at 0.6.
    /// </summary>
    public double ScoreThreshold { get; set; } = 0.5;
}
