using Dapper;

using FeedbackIntel.Infrastructure.Sql;

using Microsoft.Data.SqlClient;

namespace FeedbackIntel.Integration.Tests.Sql;

/// <summary>
/// Uses real SQL Server errors, because <see cref="SqlException"/> can't be created directly.
/// </summary>
public class SqlErrorDescriberTests
{
    private const string Secret = "jane.doe@example.com";

    [Fact]
    public async Task Describe_TruncationError_WithholdsTheQuotedValue()
    {
        var error = await CaptureAsync(
            """
            CREATE TABLE #short (value varchar(5) NOT NULL);
            INSERT INTO #short (value) VALUES (@secret);
            """);
        Assert.Contains(Secret[..5], error.Message, StringComparison.Ordinal); // SQL Server does quote it

        var description = SqlErrorDescriber.Describe(error);

        Assert.Equal(
            "SqlException: SQL Server error 2628; details withheld because they can quote data values.",
            description);
    }

    [Fact]
    public async Task Describe_ConversionErrorInsideAnotherException_WithholdsTheValue()
    {
        var error = await CaptureAsync("SELECT CAST(@secret AS int);");
        var wrapped = new InvalidOperationException("Rebuild failed", error);

        var description = SqlErrorDescriber.Describe(wrapped);

        Assert.DoesNotContain(Secret, description, StringComparison.Ordinal);
        Assert.StartsWith("InvalidOperationException: SQL Server error 245;", description, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Describe_OtherSqlError_KeepsTheMessage()
    {
        var error = await CaptureAsync("SELECT * FROM dbo.table_that_does_not_exist;");

        Assert.Equal($"SqlException: {error.Message}", SqlErrorDescriber.Describe(error));
    }

    [Fact]
    public void Describe_NonSqlError_KeepsTheMessage()
    {
        Assert.Equal(
            "HttpRequestException: GitHub returned 502",
            SqlErrorDescriber.Describe(new HttpRequestException("GitHub returned 502")));
    }

    private static async Task<SqlException> CaptureAsync(string sql)
    {
        var settings = await SqlServerTestDatabase.CreateMigratedAsync();
        await using var connection = new SqlConnection(settings.ConnectionString);
        return await Assert.ThrowsAsync<SqlException>(() => connection.ExecuteAsync(sql, new { secret = Secret }));
    }
}
