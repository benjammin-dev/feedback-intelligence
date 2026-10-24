using FeedbackIntel.Infrastructure.Sql;

using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging.Abstractions;

using Testcontainers.MsSql;

namespace FeedbackIntel.Integration.Tests.Sql;

/// <summary>
/// Starts one SQL Server container for the whole test run, and gives each test its own
/// empty database so tests can't affect each other. Testcontainers removes the
/// container automatically when the test run ends.
/// </summary>
internal static class SqlServerTestDatabase
{
    private const string Image = "mcr.microsoft.com/mssql/server:2022-latest";

    private static readonly Lazy<Task<MsSqlContainer>> Container = new(StartContainerAsync);

    /// <summary>Settings pointing at a new, uniquely named database that doesn't exist yet.</summary>
    public static async Task<DatabaseSettings> CreateSettingsAsync()
    {
        var container = await Container.Value;
        var connectionString = new SqlConnectionStringBuilder(container.GetConnectionString())
        {
            InitialCatalog = $"test_{Guid.NewGuid():N}",
        }.ConnectionString;

        return new DatabaseSettings(connectionString, TimeSpan.FromSeconds(60));
    }

    /// <summary>Settings for a new database that already has every migration applied.</summary>
    public static async Task<DatabaseSettings> CreateMigratedAsync()
    {
        var settings = await CreateSettingsAsync();
        await CreateMigrator(settings).MigrateAsync(CancellationToken.None);
        return settings;
    }

    public static SqlDatabaseMigrator CreateMigrator(DatabaseSettings settings) =>
        new(settings, NullLogger<SqlDatabaseMigrator>.Instance);

    private static async Task<MsSqlContainer> StartContainerAsync()
    {
        var container = new MsSqlBuilder(Image).Build();
        await container.StartAsync();
        return container;
    }
}
