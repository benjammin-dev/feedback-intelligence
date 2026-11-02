using Dapper;

using FeedbackIntel.Core.Quality;

using Microsoft.Data.SqlClient;

namespace FeedbackIntel.Infrastructure.Sql;

/// <summary>
/// Checks the reporting tables after they are rebuilt and before they are published
/// (FR-DAT-05): row counts, required fields, valid values, and freshness. Each check is a
/// query that counts the rows breaking one rule, so a failure says which rule and how many
/// rows, without quoting any data.
/// </summary>
public sealed class SqlQualityChecker(DatabaseSettings settings) : IQualityChecker
{
    /// <summary>
    /// Each check's description and the query that counts the rows breaking it.
    /// <c>@run_id</c> is the current run, and <c>@now</c> the current time (UTC).
    /// </summary>
    internal static readonly IReadOnlyList<(string Description, string Sql)> Checks =
    [
        // Row counts
        ("There are no cleaned issues to publish.",
            "SELECT CASE WHEN EXISTS (SELECT 1 FROM cleaned.issue) THEN 0 ELSE 1 END"),
        ("reporting.issue doesn't have exactly one row per cleaned issue.",
            "SELECT ABS((SELECT COUNT(*) FROM reporting.issue) - (SELECT COUNT(*) FROM cleaned.issue))"),
        ("reporting.overview doesn't have a current and a previous row for every repository and 'all'.",
            """
            SELECT COUNT(*) FROM
            (
                SELECT owner + '/' + name AS repository FROM cleaned.repository
                UNION ALL
                SELECT 'all'
            ) AS names
            CROSS JOIN (VALUES ('current'), ('previous')) AS periods (period)
            WHERE NOT EXISTS
            (
                SELECT 1 FROM reporting.overview AS o
                WHERE o.repository = names.repository AND o.period = periods.period
            )
            """),
        ("reporting.site_info doesn't have exactly one row from this run.",
            """
            SELECT CASE WHEN (SELECT COUNT(*) FROM reporting.site_info) = 1
                         AND EXISTS (SELECT 1 FROM reporting.site_info WHERE last_run_id = @run_id)
                        THEN 0 ELSE 1 END
            """),

        // Required fields
        ("reporting.issue has rows with an empty title.",
            "SELECT COUNT(*) FROM reporting.issue WHERE LEN(LTRIM(title)) = 0"),
        ("reporting.issue has rows whose link isn't a GitHub issue link.",
            "SELECT COUNT(*) FROM reporting.issue WHERE html_url NOT LIKE 'https://github.com/%/issues/%'"),
        ("reporting.issue has closed issues without a closing time.",
            "SELECT COUNT(*) FROM reporting.issue WHERE state = 'closed' AND closed_at IS NULL"),

        // Valid values
        ("reporting.issue has rows with a state other than open or closed.",
            "SELECT COUNT(*) FROM reporting.issue WHERE state NOT IN ('open', 'closed')"),
        ("reporting.issue has issues closed before they were created.",
            "SELECT COUNT(*) FROM reporting.issue WHERE days_to_close < 0"),
        ("reporting.overview has negative counts.",
            "SELECT COUNT(*) FROM reporting.overview WHERE new_issues < 0 OR closed_issues < 0"),
        ("reporting.weekly_metrics has weeks that don't start on a Monday.",
            "SELECT COUNT(*) FROM reporting.weekly_metrics WHERE DATEDIFF(day, '19000101', week_start) % 7 <> 0"),
        ("reporting.weekly_metrics has negative counts.",
            "SELECT COUNT(*) FROM reporting.weekly_metrics WHERE new_issues < 0 OR closed_issues < 0"),

        // Freshness
        ("reporting.site_info isn't from the last 24 hours.",
            """
            SELECT COUNT(*) FROM reporting.site_info
            WHERE data_updated_at < DATEADD(hour, -24, @now) OR data_updated_at > DATEADD(minute, 5, @now)
            """),
        ("cleaned.issue has issues updated more than a day in the future.",
            "SELECT COUNT(*) FROM cleaned.issue WHERE updated_at > DATEADD(day, 1, @now)"),
    ];

    /// <inheritdoc />
    public async Task<IReadOnlyList<string>> FindProblemsAsync(long runId, DateTime now, CancellationToken cancellationToken)
    {
        var parameters = new { run_id = runId, now };
        var problems = new List<string>();

        await using var connection = new SqlConnection(settings.ConnectionString);
        foreach (var (description, sql) in Checks)
        {
            var failures = await connection.ExecuteScalarAsync<int>(
                new CommandDefinition(sql, parameters, cancellationToken: cancellationToken));
            if (failures > 0)
            {
                problems.Add($"{description} ({failures} {(failures == 1 ? "row" : "rows")})");
            }
        }

        return problems;
    }
}
