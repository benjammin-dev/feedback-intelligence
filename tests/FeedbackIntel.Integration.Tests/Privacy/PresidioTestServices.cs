using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;

using FeedbackIntel.Core.Configuration;
using FeedbackIntel.Infrastructure.Privacy;

namespace FeedbackIntel.Integration.Tests.Privacy;

/// <summary>
/// Starts the Presidio analyzer and anonymizer once for the whole test run, using the
/// same pinned images as docker-compose.yml. Testcontainers removes them afterwards.
/// </summary>
internal static class PresidioTestServices
{
    public const string AnalyzerImage = "mcr.microsoft.com/presidio-analyzer:2.2.362@sha256:286e3fa7f3a7426e775e8564fe1870f1ba8f999d3ab8bbb8cc46a44355d9d6e9";
    public const string AnonymizerImage = "mcr.microsoft.com/presidio-anonymizer:2.2.362@sha256:a10a12a2a613d13cf29d3ad3641e3258444dd8c90403dd644a0a114c472c2483";

    private const int ContainerPort = 3000;

    /// <summary>The analyzer loads a language model at startup, which is slow on a busy runner.</summary>
    private static readonly TimeSpan StartupTimeout = TimeSpan.FromMinutes(5);

    private static readonly Lazy<Task<PrivacyOptions>> Options = new(StartAsync);

    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(60) };

    public static async Task<PresidioRedactor> CreateRedactorAsync() =>
        new(Http, await Options.Value);

    private static async Task<PrivacyOptions> StartAsync()
    {
        var analyzer = Build(AnalyzerImage);
        var anonymizer = Build(AnonymizerImage);
        await Task.WhenAll(analyzer.StartAsync(), anonymizer.StartAsync());

        var options = new PrivacyOptions
        {
            AnalyzerUrl = $"http://{analyzer.Hostname}:{analyzer.GetMappedPublicPort(ContainerPort)}",
            AnonymizerUrl = $"http://{anonymizer.Hostname}:{anonymizer.GetMappedPublicPort(ContainerPort)}",
        };

        await Task.WhenAll(WaitUntilHealthyAsync(options.AnalyzerUrl), WaitUntilHealthyAsync(options.AnonymizerUrl));
        return options;
    }

    private static IContainer Build(string image) =>
        new ContainerBuilder(image)
            .WithPortBinding(ContainerPort, assignRandomHostPort: true)
            .Build();

    /// <summary>
    /// Polls the health endpoint with short requests until it answers. Testcontainers'
    /// built-in HTTP wait sends one request with a 100-second timeout and gives up if it
    /// hangs, which happens while the analyzer is still loading on a slow runner.
    /// </summary>
    private static async Task WaitUntilHealthyAsync(string baseUrl)
    {
        using var probe = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
        var healthUrl = new Uri(new Uri(baseUrl), "health");
        var deadline = DateTime.UtcNow + StartupTimeout;

        while (true)
        {
            try
            {
                using var response = await probe.GetAsync(healthUrl);
                if (response.IsSuccessStatusCode)
                {
                    return;
                }
            }
            catch (HttpRequestException)
            {
                // Not listening yet.
            }
            catch (TaskCanceledException)
            {
                // Listening but not answering yet.
            }

            if (DateTime.UtcNow > deadline)
            {
                throw new TimeoutException($"Presidio at {baseUrl} wasn't healthy within {StartupTimeout.TotalMinutes} minutes.");
            }

            await Task.Delay(TimeSpan.FromSeconds(2));
        }
    }
}
