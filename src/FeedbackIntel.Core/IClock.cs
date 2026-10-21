namespace FeedbackIntel.Core;

/// <summary>
/// Provides the current time. Code asks this interface instead of calling
/// <see cref="DateTime.UtcNow"/> directly, so tests can control time.
/// </summary>
public interface IClock
{
    /// <summary>The current time in UTC.</summary>
    DateTime UtcNow { get; }
}
