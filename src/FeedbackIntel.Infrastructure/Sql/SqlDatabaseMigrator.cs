using DbUp;
using DbUp.Engine.Output;

using FeedbackIntel.Core.Database;

using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;

namespace FeedbackIntel.Infrastructure.Sql;

/// <summary>
/// Applies the numbered scripts in <c>database/migrations</c> using DbUp. The scripts
/// are embedded in this assembly, and DbUp records each one it applies in
/// <c>dbo.SchemaVersions</c>, so every script runs exactly once per database.
/// </summary>
public sealed class SqlDatabaseMigrator(DatabaseSettings settings, ILogger<SqlDatabaseMigrator> logger)
    : IDatabaseMigrator
{
    private const string ScriptPrefix = "Migrations.";
    private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(2);

    /// <inheritdoc />
    public async Task MigrateAsync(CancellationToken cancellationToken)
    {
        await WaitForServerAsync(cancellationToken);

        var upgradeLog = new MicrosoftUpgradeLog(logger);
        EnsureDatabase.For.SqlDatabase(settings.ConnectionString, upgradeLog);

        var upgrader = DeployChanges.To
            .SqlDatabase(settings.ConnectionString)
            .WithScriptsEmbeddedInAssembly(
                typeof(SqlDatabaseMigrator).Assembly,
                name => name.StartsWith(ScriptPrefix, StringComparison.Ordinal))
            .WithTransactionPerScript()
            .LogTo(upgradeLog)
            .Build();

        var result = upgrader.PerformUpgrade();
        if (!result.Successful)
        {
            throw new InvalidOperationException(
                $"Migration script '{result.ErrorScript?.Name}' failed.", result.Error);
        }
    }

    /// <summary>
    /// Retries a connection to the server's <c>master</c> database until it succeeds or
    /// the startup wait runs out. Only connection errors are retried.
    /// </summary>
    private async Task WaitForServerAsync(CancellationToken cancellationToken)
    {
        var master = new SqlConnectionStringBuilder(settings.ConnectionString)
        {
            InitialCatalog = "master",
            ConnectTimeout = 5,
        };

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(settings.StartupWait);

        while (true)
        {
            try
            {
                await using var connection = new SqlConnection(master.ConnectionString);
                await connection.OpenAsync(timeout.Token);
                return;
            }
            catch (SqlException ex) when (!timeout.IsCancellationRequested)
            {
                logger.LogInformation("SQL Server not ready yet ({Error}); retrying", ex.Message);
                await Task.Delay(RetryDelay, timeout.Token);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                throw new TimeoutException(
                    $"SQL Server did not accept connections within {settings.StartupWait.TotalSeconds} seconds.");
            }
        }
    }
}
