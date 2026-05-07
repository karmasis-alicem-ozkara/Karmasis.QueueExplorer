namespace KarmasisQueueExplorer.Core.Models;

/// <summary>
/// Lightweight queue metadata used by the UI tree.
/// </summary>
/// <param name="Name">Display name of the queue.</param>
/// <param name="Path">MSMQ path or format name.</param>
/// <param name="MachineName">Machine that owns the queue.</param>
/// <param name="Type">Queue category.</param>
/// <param name="MessageCount">Known message count, or null when unavailable.</param>
public sealed record QueueInfo(
    string Name,
    string Path,
    string MachineName,
    QueueType Type,
    int? MessageCount = null);
