namespace FeedbackIntel.Core.Orchestration;

/// <summary>
/// Runs the pipeline steps in order. The first failure stops the run, so
/// nothing half-finished is published. Every run, successful or not, ends
/// with its record saved.
/// </summary>
public sealed class PipelineOrchestrator(IEnumerable<IPipelineStep> steps, IRunStore runStore, IClock clock)
{
    /// <summary>Matches the size of <c>ops.run.error_message</c>.</summary>
    public const int MaxErrorMessageLength = 2000;

    /// <summary>The step name recorded when only saving the run record fails.</summary>
    public const string SaveRunRecordStepName = "save run record";

    private readonly IReadOnlyList<IPipelineStep> _steps = steps.ToList();

    /// <summary>Runs every step and returns the finished run record.</summary>
    public async Task<RunRecord> RunAsync(long runId, string trigger, CancellationToken cancellationToken)
    {
        var run = new RunRecord { RunId = runId, Trigger = trigger, StartedAt = clock.UtcNow };
        var context = new RunContext(run);

        foreach (var step in _steps)
        {
            try
            {
                await step.ExecuteAsync(context, cancellationToken);
            }
            catch (Exception ex)
            {
                MarkFailed(run, step.Name, ex);
                break;
            }
        }

        if (run.Status == RunStatus.Running)
        {
            run.Status = RunStatus.Succeeded;
        }

        run.FinishedAt = clock.UtcNow;

        try
        {
            // Not cancellable: if the run timed out, the record of that is still worth saving.
            await runStore.SaveAsync(run, CancellationToken.None);
        }
        catch (Exception ex) when (run.Status == RunStatus.Succeeded)
        {
            MarkFailed(run, SaveRunRecordStepName, ex);
        }
        catch (Exception)
        {
            // The run already failed; keep that original error rather than this one.
        }

        return run;
    }

    private static void MarkFailed(RunRecord run, string stepName, Exception ex)
    {
        run.Status = RunStatus.Failed;
        run.FailedStep = stepName;
        run.ErrorMessage = Truncate($"{ex.GetType().Name}: {ex.Message}", MaxErrorMessageLength);
    }

    private static string Truncate(string text, int maxLength) =>
        text.Length <= maxLength ? text : text[..maxLength];
}
