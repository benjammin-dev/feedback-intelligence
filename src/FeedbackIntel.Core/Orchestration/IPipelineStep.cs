namespace FeedbackIntel.Core.Orchestration;

/// <summary>One stage of the pipeline, such as ingesting issues or publishing files.</summary>
public interface IPipelineStep
{
    /// <summary>A short name, recorded in <c>ops.run.failed_step</c> if the step fails.</summary>
    string Name { get; }

    /// <summary>Does the step's work. Throws if the run should stop.</summary>
    Task ExecuteAsync(RunContext context, CancellationToken cancellationToken);
}
