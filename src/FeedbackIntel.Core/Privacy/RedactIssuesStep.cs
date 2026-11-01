using System.Collections.Concurrent;

using FeedbackIntel.Core.Configuration;
using FeedbackIntel.Core.Ingestion;
using FeedbackIntel.Core.Model;
using FeedbackIntel.Core.Orchestration;
using FeedbackIntel.Core.Text;

namespace FeedbackIntel.Core.Privacy;

/// <summary>
/// Step 4: redacts every issue fetched this run and stores it in the cleaned layer. This is
/// the only path from <c>raw</c> to <c>cleaned</c>, so no unredacted text can reach later
/// steps. Any redaction failure fails the run (design: privacy is never traded for availability).
/// </summary>
public sealed class RedactIssuesStep(
    IIssueStore rawStore,
    IPiiRedactor redactor,
    ICleanedIssueStore cleanedStore,
    TextOptions options) : IPipelineStep
{
    /// <summary>How many issues are redacted at once. Presidio handles a few requests in parallel.</summary>
    public const int MaxParallelIssues = 4;

    /// <inheritdoc />
    public string Name => "redact issues";

    /// <inheritdoc />
    public async Task ExecuteAsync(RunContext context, CancellationToken cancellationToken)
    {
        var rawIssues = await rawStore.ReadRawIssuesAsync(cancellationToken);
        var cleaned = new ConcurrentBag<CleanedIssue>();

        await Parallel.ForEachAsync(
            rawIssues,
            new ParallelOptions { MaxDegreeOfParallelism = MaxParallelIssues, CancellationToken = cancellationToken },
            async (issue, token) => cleaned.Add(await CleanAsync(issue, token)));

        await cleanedStore.UpsertAsync(cleaned.ToList(), context.Run.RunId, cancellationToken);
        context.Run.IssuesRedacted = cleaned.Count;
    }

    /// <summary>
    /// Redacts the title and the full body, hashes them, and only then truncates the body.
    /// Hashing before truncation means a change after the first 4,000 characters still
    /// changes the hash, so the AI step knows to reprocess the issue.
    /// </summary>
    internal async Task<CleanedIssue> CleanAsync(RawIssue issue, CancellationToken cancellationToken)
    {
        var title = await redactor.RedactAsync(issue.Title, cancellationToken);
        var body = await redactor.RedactAsync(issue.Body ?? "", cancellationToken);

        var hash = ContentHasher.Compute(title.Text, body.Text);
        var truncated = body.Text.Length > options.BodyMaxCharacters;

        return new CleanedIssue(
            RepoId: issue.RepoId,
            IssueNumber: issue.IssueNumber,
            TitleRedacted: Truncate(title.Text, RawIssueValidator.MaxTitleLength),
            BodyRedacted: issue.Body is null ? null : Truncate(body.Text, options.BodyMaxCharacters),
            BodyLength: issue.Body?.Length ?? 0,
            BodyTruncated: truncated,
            ContentHash: hash,
            RedactionCount: (short)Math.Min(title.RedactionCount + body.RedactionCount, short.MaxValue),
            State: issue.State,
            StateReason: issue.StateReason,
            LabelsJson: issue.LabelsJson,
            CreatedAt: issue.CreatedAt,
            UpdatedAt: issue.UpdatedAt,
            ClosedAt: issue.ClosedAt,
            HtmlUrl: issue.HtmlUrl);
    }

    private static string Truncate(string text, int maxLength) =>
        text.Length <= maxLength ? text : text[..maxLength];
}
