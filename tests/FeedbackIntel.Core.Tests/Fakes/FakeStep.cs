using FeedbackIntel.Core.Orchestration;

namespace FeedbackIntel.Core.Tests.Fakes;

/// <summary>A step that records that it ran, then runs optional test code.</summary>
internal sealed class FakeStep(string name, List<string> log, Action<RunContext>? action = null) : IPipelineStep
{
    public string Name => name;

    public Task ExecuteAsync(RunContext context, CancellationToken cancellationToken)
    {
        log.Add(name);
        action?.Invoke(context);
        return Task.CompletedTask;
    }
}
