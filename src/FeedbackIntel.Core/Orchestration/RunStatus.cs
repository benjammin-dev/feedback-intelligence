namespace FeedbackIntel.Core.Orchestration;

/// <summary>The state of a pipeline run, stored in <c>ops.run.status</c>.</summary>
public enum RunStatus
{
    /// <summary>The run has started and not yet finished.</summary>
    Running,

    /// <summary>Every step completed.</summary>
    Succeeded,

    /// <summary>A step failed, so the run stopped.</summary>
    Failed,
}
