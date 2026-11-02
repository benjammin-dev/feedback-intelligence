using FeedbackIntel.Core.Orchestration;

using Microsoft.Data.SqlClient;

namespace FeedbackIntel.Infrastructure.Sql;

/// <summary>
/// Describes a failure for the run record and the public alert issue, leaving out database
/// errors whose messages can quote the data that caused them. Issue text that reached such a
/// message could include personal information, so only the error number is kept.
/// </summary>
public static class SqlErrorDescriber
{
    /// <summary>SQL Server errors whose messages can include data values.</summary>
    internal static readonly IReadOnlySet<int> ValueQuotingErrors = new HashSet<int>
    {
        241,  // Conversion failed when converting date and/or time from character string.
        242,  // The conversion of a data type resulted in an out-of-range value.
        245,  // Conversion failed when converting the value '...' to data type ...
        295,  // Conversion failed when converting character string to smalldatetime.
        2601, // Cannot insert duplicate key row ... The duplicate key value is (...).
        2627, // Violation of PRIMARY KEY or UNIQUE constraint ... The duplicate key value is (...).
        2628, // String or binary data would be truncated ... Truncated value: '...'.
        8114, // Error converting data type ... to ...
        8152, // String or binary data would be truncated.
    };

    /// <summary>
    /// The default description, unless a value-quoting SQL Server error is found anywhere in
    /// the exception or its inner exceptions.
    /// </summary>
    public static string Describe(Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (current is SqlException sql && sql.Errors.Cast<SqlError>().Any(error => ValueQuotingErrors.Contains(error.Number)))
            {
                return $"{exception.GetType().Name}: SQL Server error {sql.Number}; details withheld because they can quote data values.";
            }
        }

        return PipelineOrchestrator.DescribeError(exception);
    }
}
