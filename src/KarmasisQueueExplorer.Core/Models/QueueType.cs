namespace KarmasisQueueExplorer.Core.Models;

/// <summary>
/// Describes the MSMQ queue category displayed in the explorer tree.
/// </summary>
public enum QueueType
{
    Private,
    Public,
    System,
    Journal,
    DeadLetter,
    Unknown
}
