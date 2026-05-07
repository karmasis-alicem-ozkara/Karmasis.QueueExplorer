namespace KarmasisQueueExplorer.Core.Services;

/// <summary>
/// Indicates that local MSMQ metadata could not be found and the MSMQ Windows Feature may be disabled.
/// </summary>
public sealed class MsmqUnavailableException(string message) : InvalidOperationException(message);
