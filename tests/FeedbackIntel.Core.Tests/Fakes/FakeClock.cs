using FeedbackIntel.Core;

namespace FeedbackIntel.Core.Tests.Fakes;

/// <summary>A clock that only moves when a test tells it to.</summary>
internal sealed class FakeClock(DateTime start) : IClock
{
    public DateTime UtcNow { get; private set; } = start;

    public void Advance(TimeSpan amount) => UtcNow += amount;
}
