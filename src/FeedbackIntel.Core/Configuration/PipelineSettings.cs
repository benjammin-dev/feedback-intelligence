namespace FeedbackIntel.Core.Configuration;

/// <summary>
/// Everything in <c>config/pipeline.json</c>. Sections arrive as milestones need them;
/// secrets and the connection string never appear here (NFR-SEC-01).
/// </summary>
public sealed class PipelineSettings
{
    /// <summary>The repositories the pipeline knows about.</summary>
    public List<RepositoryOptions> Repositories { get; set; } = [];

    /// <summary>How issues are fetched and validated.</summary>
    public IngestionOptions Ingestion { get; set; } = new();

    /// <summary>Operational limits.</summary>
    public OperationsOptions Operations { get; set; } = new();

    /// <summary>Where output is written.</summary>
    public PublishingOptions Publishing { get; set; } = new();

    /// <summary>Where the redaction services are, and how sensitive they are.</summary>
    public PrivacyOptions Privacy { get; set; } = new();

    /// <summary>How issue text is kept.</summary>
    public TextOptions Text { get; set; } = new();

    /// <summary>
    /// Returns every problem with the settings, so a bad config file fails at startup
    /// with a clear message instead of partway through a run (FR-CFG-01).
    /// </summary>
    public IReadOnlyList<string> FindProblems()
    {
        var problems = new List<string>();

        if (!Repositories.Any(repository => repository.Enabled))
        {
            problems.Add("Repositories: at least one repository must be enabled.");
        }

        if (Repositories.Any(repository => string.IsNullOrWhiteSpace(repository.Owner) || string.IsNullOrWhiteSpace(repository.Name)))
        {
            problems.Add("Repositories: every entry needs an Owner and a Name.");
        }

        if (Ingestion.InitialHistoryDays is < 1 or > 365)
        {
            problems.Add("Ingestion:InitialHistoryDays must be between 1 and 365.");
        }

        if (Ingestion.MaxRejectedPercent is < 0 or > 100)
        {
            problems.Add("Ingestion:MaxRejectedPercent must be between 0 and 100.");
        }

        if (Operations.SqlStartupWaitSeconds is < 1 or > 600)
        {
            problems.Add("Operations:SqlStartupWaitSeconds must be between 1 and 600.");
        }

        if (string.IsNullOrWhiteSpace(Publishing.OutputDirectory))
        {
            problems.Add("Publishing:OutputDirectory must not be empty.");
        }

        if (!IsHttpUrl(Privacy.AnalyzerUrl) || !IsHttpUrl(Privacy.AnonymizerUrl))
        {
            problems.Add("Privacy:AnalyzerUrl and Privacy:AnonymizerUrl must be absolute http or https URLs.");
        }

        if (Privacy.ScoreThreshold is < 0 or > 1)
        {
            problems.Add("Privacy:ScoreThreshold must be between 0 and 1.");
        }

        if (Text.BodyMaxCharacters is < 1 or > 4000)
        {
            problems.Add("Text:BodyMaxCharacters must be between 1 and 4000, the size of cleaned.issue.body_redacted.");
        }

        return problems;
    }

    private static bool IsHttpUrl(string value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
}
