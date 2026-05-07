using System.Windows;
using KarmasisQueueExplorer.Core.Services;

namespace KarmasisQueueExplorer.App.Services;

public sealed class WpfDialogService : IDialogService
{
    public Task<bool> ConfirmAsync(string title, string message, CancellationToken cancellationToken = default)
    {
        var result = MessageBox.Show(message, title, MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No);
        return Task.FromResult(result == MessageBoxResult.Yes);
    }
}
