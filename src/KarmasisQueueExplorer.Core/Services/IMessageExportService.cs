using KarmasisQueueExplorer.Core.Models;

namespace KarmasisQueueExplorer.Core.Services;

/// <summary>
/// Exports message content to local files without coupling ViewModels to WPF dialogs.
/// </summary>
public interface IMessageExportService
{
    /// <summary>
    /// Exports the selected message to a readable text file.
    /// </summary>
    Task<string> ExportMessageAsync(MessageInfo message, string? queuePath, CancellationToken cancellationToken = default);
}
