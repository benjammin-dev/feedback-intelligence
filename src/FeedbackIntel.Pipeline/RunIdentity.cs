namespace FeedbackIntel.Pipeline;

/// <summary>Works out the run ID and trigger from the environment GitHub Actions provides.</summary>
internal static class RunIdentity
{
    /// <summary>
    /// In GitHub Actions, the run ID links the run record to its logs. Locally there is
    /// no run ID, so the current Unix time in seconds stands in as a unique number.
    /// </summary>
    public static (long RunId, string Trigger) FromEnvironment()
    {
        var runId = long.TryParse(Environment.GetEnvironmentVariable("GITHUB_RUN_ID"), out var githubRunId)
            ? githubRunId
            : DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        var trigger = Environment.GetEnvironmentVariable("GITHUB_EVENT_NAME") == "schedule" ? "schedule" : "manual";

        return (runId, trigger);
    }
}
