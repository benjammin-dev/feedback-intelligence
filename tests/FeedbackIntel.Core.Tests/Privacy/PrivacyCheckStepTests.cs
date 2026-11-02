using FeedbackIntel.Core.Orchestration;
using FeedbackIntel.Core.Privacy;

namespace FeedbackIntel.Core.Tests.Privacy;

public class PrivacyCheckStepTests
{
    private static RunContext Context() =>
        new(new RunRecord { RunId = 1, Trigger = "schedule", StartedAt = new DateTime(2026, 11, 1, 13, 17, 0, DateTimeKind.Utc) });

    [Fact]
    public void TestSet_EverySampleContainsItsSecret()
    {
        Assert.All(PiiTestSet.Samples, sample => Assert.Contains(sample.Secret, sample.Text, StringComparison.Ordinal));
    }

    [Fact]
    public async Task ExecuteAsync_RedactorRemovesEverything_Passes()
    {
        var redactor = new FakeRedactor(missPlaceholder: null);

        await new PrivacyCheckStep(redactor).ExecuteAsync(Context(), CancellationToken.None);

        Assert.Equal(PiiTestSet.Samples.Count, redactor.Calls);
    }

    [Fact]
    public async Task ExecuteAsync_RedactorMissesAKind_ThrowsNamingKindNotValue()
    {
        var redactor = new FakeRedactor(missPlaceholder: "<EMAIL_ADDRESS>");

        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => new PrivacyCheckStep(redactor).ExecuteAsync(Context(), CancellationToken.None));

        Assert.Contains("missed 1 of", error.Message);
        Assert.Contains("(EMAIL_ADDRESS)", error.Message);
        Assert.DoesNotContain("@", error.Message);
    }

    /// <summary>Replaces each sample's secret with its placeholder, except for one kind if asked.</summary>
    private sealed class FakeRedactor(string? missPlaceholder) : IPiiRedactor
    {
        public int Calls { get; private set; }

        public Task<RedactedText> RedactAsync(string text, CancellationToken cancellationToken)
        {
            Calls++;
            var sample = PiiTestSet.Samples.Single(candidate => candidate.Text == text);
            return Task.FromResult(sample.Placeholder == missPlaceholder
                ? new RedactedText(text, 0)
                : new RedactedText(text.Replace(sample.Secret, sample.Placeholder, StringComparison.Ordinal), 1));
        }
    }
}
