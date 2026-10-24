using FeedbackIntel.Core;
using FeedbackIntel.Core.Configuration;
using FeedbackIntel.Core.Database;
using FeedbackIntel.Core.Ingestion;
using FeedbackIntel.Core.Orchestration;
using FeedbackIntel.Core.Publishing;
using FeedbackIntel.Core.Reporting;
using FeedbackIntel.Infrastructure;
using FeedbackIntel.Infrastructure.GitHub;
using FeedbackIntel.Infrastructure.Publishing;
using FeedbackIntel.Infrastructure.Sql;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using Octokit;

namespace FeedbackIntel.Pipeline;

/// <summary>
/// The composition root: the one place that knows which real implementation backs each
/// interface. Swapping a provider means changing a line here, not in Core (NFR-MNT-03).
/// </summary>
internal static class PipelineServices
{
    private const string ConnectionStringName = "FeedbackIntel";

    public static ServiceProvider Build(IConfiguration configuration)
    {
        var settings = LoadSettings(configuration);
        var connectionString = configuration.GetConnectionString(ConnectionStringName)
            ?? throw new InvalidOperationException(
                $"Set the ConnectionStrings__{ConnectionStringName} environment variable.");

        var services = new ServiceCollection();

        services.AddLogging(logging => logging.AddSimpleConsole(options =>
        {
            options.IncludeScopes = true;
            options.SingleLine = true;
            options.UseUtcTimestamp = true;
            options.TimestampFormat = "HH:mm:ss ";
        }));

        // Settings
        services.AddSingleton<IReadOnlyList<RepositoryOptions>>(settings.Repositories);
        services.AddSingleton(settings.Ingestion);
        services.AddSingleton(settings.Publishing);
        services.AddSingleton(new DatabaseSettings(
            connectionString, TimeSpan.FromSeconds(settings.Operations.SqlStartupWaitSeconds)));

        // Adapters: real services behind Core's interfaces
        services.AddSingleton<IClock, SystemClock>();
        services.AddSingleton<IGitHubClient>(_ => CreateGitHubClient(configuration["GITHUB_TOKEN"]));
        services.AddSingleton<IDatabaseMigrator, SqlDatabaseMigrator>();
        services.AddSingleton<IRunStore, SqlRunStore>();
        services.AddSingleton<IIssueSource, OctokitIssueSource>();
        services.AddSingleton<IIssueStore, SqlIssueStore>();
        services.AddSingleton<IReportingBuilder, SqlReportingBuilder>();
        services.AddSingleton<IPublisher, JsonFilePublisher>();

        // Steps, in the order they run (numbers match the design's step table)
        services.AddSingleton<MigrateDatabaseStep>();   // 1
        services.AddSingleton<IngestIssuesStep>();      // 3
        services.AddSingleton<RebuildReportingStep>();  // 6
        services.AddSingleton<PublishStep>();           // 9

        services.AddSingleton(provider =>
        {
            var logger = provider.GetRequiredService<ILoggerFactory>().CreateLogger("FeedbackIntel.Pipeline.Step");
            IPipelineStep[] steps =
            [
                provider.GetRequiredService<MigrateDatabaseStep>(),
                provider.GetRequiredService<IngestIssuesStep>(),
                provider.GetRequiredService<RebuildReportingStep>(),
                provider.GetRequiredService<PublishStep>(),
            ];

            return new PipelineOrchestrator(
                steps.Select(step => new LoggingStep(step, logger)),
                provider.GetRequiredService<IRunStore>(),
                provider.GetRequiredService<IClock>());
        });

        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true });
    }

    private static PipelineSettings LoadSettings(IConfiguration configuration)
    {
        var settings = configuration.Get<PipelineSettings>() ?? new PipelineSettings();
        var problems = settings.FindProblems();
        if (problems.Count > 0)
        {
            throw new InvalidOperationException("Invalid configuration: " + string.Join(" ", problems));
        }

        return settings;
    }

    /// <summary>
    /// Uses the token when one is set. GitHub Actions provides one automatically; locally,
    /// <c>gh auth token</c> supplies yours. Without a token GitHub allows 60 requests an hour.
    /// </summary>
    private static GitHubClient CreateGitHubClient(string? token)
    {
        var client = new GitHubClient(new ProductHeaderValue("feedback-intelligence"));
        if (!string.IsNullOrWhiteSpace(token))
        {
            client.Credentials = new Credentials(token);
        }

        return client;
    }
}
