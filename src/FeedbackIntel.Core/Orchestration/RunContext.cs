namespace FeedbackIntel.Core.Orchestration;

/// <summary>
/// What every step receives while the pipeline runs. Steps pass data to each
/// other through the database, so this only carries the run's own details.
/// </summary>
/// <param name="Run">The run record that steps update as they work.</param>
public sealed record RunContext(RunRecord Run);
