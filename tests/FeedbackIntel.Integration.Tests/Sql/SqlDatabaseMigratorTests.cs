using Dapper;

using Microsoft.Data.SqlClient;

namespace FeedbackIntel.Integration.Tests.Sql;

public class SqlDatabaseMigratorTests
{
    [Fact]
    public async Task MigrateAsync_NewDatabase_CreatesSkeletonTables()
    {
        var settings = await SqlServerTestDatabase.CreateMigratedAsync();

        await using var connection = new SqlConnection(settings.ConnectionString);
        var tables = await connection.QueryAsync<string>(
            "SELECT TABLE_SCHEMA + '.' + TABLE_NAME FROM INFORMATION_SCHEMA.TABLES");

        Assert.Contains("cleaned.repository", tables);
        Assert.Contains("raw.issue", tables);
        Assert.Contains("ops.run", tables);
    }

    [Fact]
    public async Task MigrateAsync_RunTwice_AppliesEachScriptOnce()
    {
        var settings = await SqlServerTestDatabase.CreateMigratedAsync();

        await SqlServerTestDatabase.CreateMigrator(settings).MigrateAsync(CancellationToken.None);

        await using var connection = new SqlConnection(settings.ConnectionString);
        var applied = await connection.QueryAsync<string>("SELECT ScriptName FROM dbo.SchemaVersions");
        Assert.Equal(applied.Count(), applied.Distinct().Count());
    }
}
