using FeedbackIntel.Core.Orchestration;

namespace FeedbackIntel.Core.Publishing;

/// <summary>Step 9: writes the dashboard data files. Runs only if every earlier step succeeded.</summary>
public sealed class PublishStep(IPublisher publisher) : IPipelineStep
{
    /// <inheritdoc />
    public string Name => "publish";

    /// <inheritdoc />
    public Task ExecuteAsync(RunContext context, CancellationToken cancellationToken) =>
        publisher.PublishAsync(cancellationToken);
}
