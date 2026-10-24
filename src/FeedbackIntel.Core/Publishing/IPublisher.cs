namespace FeedbackIntel.Core.Publishing;

/// <summary>Writes the dashboard's data files from the reporting layer.</summary>
public interface IPublisher
{
    /// <summary>Writes every published file, replacing earlier versions.</summary>
    Task PublishAsync(CancellationToken cancellationToken);
}
