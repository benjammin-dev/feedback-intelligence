using FeedbackIntel.Core.Orchestration;
using FeedbackIntel.Pipeline;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

// Settings come from config/pipeline.json, and any value can be overridden by an environment
// variable (for example Publishing__OutputDirectory). Secrets only ever come from the environment.
var configuration = new ConfigurationBuilder()
    .AddJsonFile(Path.GetFullPath(Environment.GetEnvironmentVariable("PIPELINE_CONFIG") ?? "config/pipeline.json"), optional: false)
    .AddEnvironmentVariables()
    .Build();

using var services = PipelineServices.Build(configuration);
var logger = services.GetRequiredService<ILoggerFactory>().CreateLogger("FeedbackIntel.Pipeline");

using var cancellation = new CancellationTokenSource();
Console.CancelKeyPress += (_, args) =>
{
    args.Cancel = true;
    cancellation.Cancel();
};

var (runId, trigger) = RunIdentity.FromEnvironment();

using (logger.BeginScope("Run {RunId}", runId))
{
    logger.LogInformation("Pipeline starting ({Trigger})", trigger);

    var run = await services.GetRequiredService<PipelineOrchestrator>().RunAsync(runId, trigger, cancellation.Token);

    if (run.Status == RunStatus.Succeeded)
    {
        logger.LogInformation(
            "Run succeeded: {Fetched} issues fetched, {Rejected} rejected", run.IssuesFetched, run.IssuesRejected);
        return 0;
    }

    logger.LogError("Run failed in step '{Step}': {Error}", run.FailedStep, run.ErrorMessage);
    return 1;
}
