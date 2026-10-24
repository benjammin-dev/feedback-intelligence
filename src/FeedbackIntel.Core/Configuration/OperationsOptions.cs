namespace FeedbackIntel.Core.Configuration;

/// <summary>The <c>Operations</c> section of <c>config/pipeline.json</c>.</summary>
public sealed class OperationsOptions
{
    /// <summary>How long to wait for SQL Server to accept connections when the run starts.</summary>
    public int SqlStartupWaitSeconds { get; set; } = 120;
}
