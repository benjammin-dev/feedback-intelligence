namespace FeedbackIntel.Infrastructure.Sql;

/// <summary>How to reach SQL Server.</summary>
/// <param name="ConnectionString">
/// The connection string, including the database name. Comes from an environment
/// variable, never from the repository, because it contains a password.
/// </param>
/// <param name="StartupWait">
/// How long to wait for SQL Server to accept connections. On a fresh runner the
/// container can take a while to start.
/// </param>
public sealed record DatabaseSettings(string ConnectionString, TimeSpan StartupWait);
