using KarmasisQueueExplorer.Core.Models;

namespace KarmasisQueueExplorer.Core.Services;

/// <summary>
/// Provides all MSMQ access for the application.
/// </summary>
public interface IMsmqService
{
    /// <summary>
    /// Gets queues for the specified machine without loading message bodies.
    /// </summary>
    /// <param name="machineName">Target machine name. Use <c>.</c> for local machine.</param>
    /// <param name="cancellationToken">Cancellation token for the operation.</param>
    Task<IReadOnlyList<QueueInfo>> GetQueuesAsync(string machineName, CancellationToken cancellationToken = default);

    /// <summary>
    /// Peeks message metadata from the specified queue without removing messages.
    /// </summary>
    Task<IReadOnlyList<MessageInfo>> GetMessagesAsync(string queuePath, int maxCount = 100, CancellationToken cancellationToken = default);
}
