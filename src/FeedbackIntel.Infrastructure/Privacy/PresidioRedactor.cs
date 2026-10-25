using System.Net.Http.Json;
using System.Text.Json;

using FeedbackIntel.Core.Configuration;
using FeedbackIntel.Core.Privacy;

namespace FeedbackIntel.Infrastructure.Privacy;

/// <summary>
/// Redacts personal information using Microsoft Presidio's analyzer and anonymizer
/// containers over HTTP (ADR 0007). The analyzer finds personal information; the
/// anonymizer replaces it, resolving any overlapping findings.
/// </summary>
/// <remarks>
/// Only the entity types FR-PRV-01 requires are requested. Presidio's general recognizers
/// for people's names and URLs are deliberately not used: they flag product names and
/// ordinary links, which would damage titles without protecting anyone.
/// </remarks>
public sealed class PresidioRedactor(HttpClient http, PrivacyOptions options) : IPiiRedactor
{
    public const string GitHubTokenEntity = "GITHUB_TOKEN";
    public const string PathUsernameEntity = "PATH_USERNAME";

    /// <summary>Everything this redactor removes (FR-PRV-01).</summary>
    public static readonly IReadOnlyList<string> Entities =
        ["EMAIL_ADDRESS", "IP_ADDRESS", GitHubTokenEntity, PathUsernameEntity];

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    };

    /// <summary>
    /// Patterns Presidio doesn't recognize on its own. They are sent with every request,
    /// so the standard Presidio images work unchanged. Presidio matches them ignoring case.
    /// </summary>
    private static readonly IReadOnlyList<Recognizer> CustomRecognizers =
    [
        new(
            Name: "github_token",
            SupportedEntity: GitHubTokenEntity,
            Patterns:
            [
                // Classic (ghp_, gho_, ghu_, ghs_, ghr_) and fine-grained (github_pat_) tokens.
                new("github_token", @"\b(?:gh[pousr]_[a-z0-9]{36,255}|github_pat_[a-z0-9_]{22,255})\b", 0.9),
            ]),
        new(
            Name: "path_username",
            SupportedEntity: PathUsernameEntity,
            Patterns:
            [
                // Only the user name is replaced, so the rest of the path stays readable:
                // C:\Users\jsmith\AppData becomes C:\Users\<PATH_USERNAME>\AppData.
                new("windows_path", @"(?<=[a-z]:\\users\\)[^\\/\s""'<>]+", 0.9),
                new("windows_path_escaped", @"(?<=[a-z]:\\\\users\\\\)[^\\/\s""'<>]+", 0.9),
                new("unix_home", @"(?<=/home/)[^\\/\s""'<>]+", 0.9),
                new("mac_or_slash_users", @"(?<=/users/)[^\\/\s""'<>]+", 0.9),
            ]),
    ];

    /// <inheritdoc />
    public async Task<RedactedText> RedactAsync(string text, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return new RedactedText(text, 0);
        }

        var findings = await PostAsync<List<Finding>>(
            new Uri(new Uri(options.AnalyzerUrl), "analyze"),
            new AnalyzeRequest(text, "en", Entities, options.ScoreThreshold, CustomRecognizers),
            cancellationToken);

        if (findings.Count == 0)
        {
            return new RedactedText(text, 0);
        }

        var result = await PostAsync<AnonymizeResponse>(
            new Uri(new Uri(options.AnonymizerUrl), "anonymize"),
            new AnonymizeRequest(text, findings),
            cancellationToken);

        return new RedactedText(result.Text, result.Items.Count);
    }

    /// <remarks>
    /// Error messages contain only the status code, never the text, because they can reach
    /// public run logs. Any failure stops the run: the pipeline never continues unredacted.
    /// </remarks>
    private async Task<T> PostAsync<T>(Uri uri, object body, CancellationToken cancellationToken)
    {
        using var response = await http.PostAsJsonAsync(uri, body, JsonOptions, cancellationToken);
        response.EnsureSuccessStatusCode();

        return await response.Content.ReadFromJsonAsync<T>(JsonOptions, cancellationToken)
            ?? throw new InvalidOperationException($"Presidio returned an empty response from {uri.AbsolutePath}.");
    }

    private sealed record AnalyzeRequest(
        string Text,
        string Language,
        IReadOnlyList<string> Entities,
        double ScoreThreshold,
        IReadOnlyList<Recognizer> AdHocRecognizers);

    private sealed record Recognizer(string Name, string SupportedEntity, IReadOnlyList<Pattern> Patterns)
    {
        public string SupportedLanguage => "en";
    }

    private sealed record Pattern(string Name, string Regex, double Score);

    private sealed record Finding(string EntityType, int Start, int End, double Score);

    private sealed record AnonymizeRequest(string Text, IReadOnlyList<Finding> AnalyzerResults);

    private sealed record AnonymizeResponse(string Text, IReadOnlyList<JsonElement> Items);
}
