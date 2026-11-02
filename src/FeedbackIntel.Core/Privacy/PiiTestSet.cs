namespace FeedbackIntel.Core.Privacy;

/// <summary>
/// The PII test set (FR-PRV-04): issue text with known personal information, one sample per
/// kind of value the redactor must remove (FR-PRV-01). CI runs it through the whole pipeline,
/// and every run checks the live redaction service with it before redacting real issues.
/// When a missed pattern is found, it gets a sample here (US-18).
/// </summary>
/// <remarks>
/// Every value is made up. The tokens are built at runtime, so the source never contains
/// anything that looks like a real token to secret scanning.
/// </remarks>
public static class PiiTestSet
{
    private static readonly string ClassicToken = "ghp_" + new string('A', 36);
    private static readonly string FineGrainedToken = "github_pat_" + new string('B', 22) + "_" + new string('c', 59);

    /// <summary>The samples, each with the value that must disappear and the placeholder that replaces it.</summary>
    public static IReadOnlyList<PiiSample> Samples { get; } =
    [
        new("Please email logs to jane.doe@example.com when you can", "jane.doe@example.com", "<EMAIL_ADDRESS>"),
        new("The proxy at 192.168.1.20 refused the connection", "192.168.1.20", "<IP_ADDRESS>"),
        new("Listening on fe80::1ff:fe23:4567:890a failed", "fe80::1ff:fe23:4567:890a", "<IP_ADDRESS>"),
        new($"I set GH_TOKEN={ClassicToken} and it still fails", ClassicToken, "<GITHUB_TOKEN>"),
        new($"Token {FineGrainedToken} was rejected", FineGrainedToken, "<GITHUB_TOKEN>"),
        new(@"Log written to C:\Users\jsmith\AppData\Local\Temp\WinGet\defaultState.log", "jsmith", "<PATH_USERNAME>"),
        new(@"""path"": ""C:\\Users\\jsmith\\Downloads\\setup.exe""", "jsmith", "<PATH_USERNAME>"),
        new("Config is in /home/jsmith/.config/app/settings.json", "jsmith", "<PATH_USERNAME>"),
        new("Crash report saved to /Users/jsmith/Library/Logs/report.txt", "jsmith", "<PATH_USERNAME>"),
    ];
}

/// <summary>One test-set sample.</summary>
/// <param name="Text">Issue text containing the value.</param>
/// <param name="Secret">The personal information that must not survive redaction.</param>
/// <param name="Placeholder">What should replace it, such as <c>&lt;EMAIL_ADDRESS&gt;</c>.</param>
public sealed record PiiSample(string Text, string Secret, string Placeholder);
