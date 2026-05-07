namespace KarmasisQueueExplorer.Core.Services;

/// <summary>
/// UI-independent dialog abstraction for confirmations and notifications.
/// </summary>
public interface IDialogService
{
    /// <summary>
    /// Asks the user to confirm a potentially destructive action.
    /// </summary>
    Task<bool> ConfirmAsync(string title, string message, CancellationToken cancellationToken = default);
}
