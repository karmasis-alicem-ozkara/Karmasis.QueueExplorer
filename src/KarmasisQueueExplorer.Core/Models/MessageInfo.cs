namespace KarmasisQueueExplorer.Core.Models;

/// <summary>
/// Lightweight message metadata for the selected queue's message list.
/// </summary>
public sealed record MessageInfo(
    string Id,
    string Label,
    DateTime? SentTime,
    long? BodySize,
    string Priority,
    string MessageClass);
