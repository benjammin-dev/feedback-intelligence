using System.Security.Cryptography;
using System.Text;

namespace FeedbackIntel.Core.Text;

/// <summary>
/// Computes a stable SHA-256 fingerprint of an issue's redacted title and body.
/// </summary>
/// <remarks>
/// The fingerprint detects real content changes, so text that hasn't changed is never
/// sent to the AI twice (FR-AI-03). Line endings and trailing whitespace are normalized
/// first, so edits that are invisible to readers don't change the hash.
/// </remarks>
public static class ContentHasher
{
    /// <summary>Computes the 32-byte SHA-256 fingerprint of a title and optional body.</summary>
    /// <param name="title">The redacted issue title.</param>
    /// <param name="body">The redacted issue body; <c>null</c> is treated as empty.</param>
    /// <returns>A 32-byte hash, matching <c>cleaned.issue.content_hash</c>.</returns>
    public static byte[] Compute(string title, string? body)
    {
        ArgumentNullException.ThrowIfNull(title);

        var normalizedTitle = Normalize(title);
        var normalizedBody = Normalize(body ?? string.Empty);

        // Prefix the title with its length so the boundary between title and body is
        // unambiguous: ("a b", "c") and ("a", "b c") must never produce the same hash.
        var combined = $"{normalizedTitle.Length}:{normalizedTitle}\n{normalizedBody}";
        return SHA256.HashData(Encoding.UTF8.GetBytes(combined));
    }

    /// <summary>
    /// Converts all line endings to <c>\n</c>, trims trailing whitespace from each line,
    /// and removes leading and trailing blank lines.
    /// </summary>
    internal static string Normalize(string text)
    {
        var lines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        return string.Join('\n', lines.Select(line => line.TrimEnd())).Trim('\n');
    }
}
