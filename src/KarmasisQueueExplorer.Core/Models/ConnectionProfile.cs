namespace KarmasisQueueExplorer.Core.Models;

/// <summary>
/// Represents a named MSMQ machine connection.
/// </summary>
public sealed record ConnectionProfile(string DisplayName, string MachineName)
{
    public static ConnectionProfile Local { get; } = new("Local Machine", ".");
}
