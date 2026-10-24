using FeedbackIntel.Core;

namespace FeedbackIntel.Infrastructure;

/// <summary>The real clock, used everywhere except tests.</summary>
public sealed class SystemClock : IClock
{
    /// <inheritdoc />
    public DateTime UtcNow => DateTime.UtcNow;
}
