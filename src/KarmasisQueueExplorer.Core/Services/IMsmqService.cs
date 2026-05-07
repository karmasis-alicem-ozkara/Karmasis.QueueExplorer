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

    /// <summary>
    /// Sends a new text message to the specified queue.
    /// </summary>
    Task SendMessageAsync(string queuePath, string label, string bodyText, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes a message by receiving it explicitly by id. This is destructive and must be called only after confirmation.
    /// </summary>
    Task DeleteMessageAsync(string queuePath, string messageId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Copies message label and readable body to another queue without removing the source message.
    /// </summary>
    Task CopyMessageAsync(string targetQueuePath, MessageInfo message, CancellationToken cancellationToken = default);

    /// <summary>
    /// Purges all messages from a queue. This is destructive and must be called only after confirmation.
    /// </summary>
    Task PurgeQueueAsync(string queuePath, CancellationToken cancellationToken = default);
}
