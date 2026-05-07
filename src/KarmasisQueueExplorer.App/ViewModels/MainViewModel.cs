using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using KarmasisQueueExplorer.Core.Services;
using KarmasisQueueExplorer.Core.Models;

namespace KarmasisQueueExplorer.App.ViewModels;

public sealed partial class MainViewModel(IMsmqService msmqService) : ObservableObject
{
    [ObservableProperty]
    private ObservableCollection<QueueNodeViewModel> _queues = [];

    [ObservableProperty]
    private ObservableCollection<QueueGroupViewModel> _queueGroups = [];

    [ObservableProperty]
    private QueueNodeViewModel? _selectedQueue;

    [ObservableProperty]
    private object? _selectedTreeItem;

    [ObservableProperty]
    private string _statusMessage = "Ready";

    [ObservableProperty]
    private string _warningMessage = string.Empty;

    public bool HasWarning => !string.IsNullOrWhiteSpace(WarningMessage);

    [ObservableProperty]
    private bool _isBusy;

    partial void OnWarningMessageChanged(string value)
    {
        OnPropertyChanged(nameof(HasWarning));
    }

    partial void OnSelectedTreeItemChanged(object? value)
    {
        SelectedQueue = value as QueueNodeViewModel;
    }

    partial void OnSelectedQueueChanged(QueueNodeViewModel? value)
    {
        if (value is not null)
        {
            StatusMessage = $"Selected queue: {value.Path}";
        }
    }

    [RelayCommand]
    private async Task LoadLocalQueuesAsync(CancellationToken cancellationToken)
    {
        try
        {
            IsBusy = true;
            WarningMessage = string.Empty;
            StatusMessage = "Loading local MSMQ queues...";

            var queues = await msmqService.GetQueuesAsync(".", cancellationToken);
            Queues = new ObservableCollection<QueueNodeViewModel>(queues.Select(queue => new QueueNodeViewModel(queue)));
            QueueGroups = BuildQueueGroups(Queues);
            StatusMessage = Queues.Count == 0
                ? "No local MSMQ queues found."
                : $"Loaded {Queues.Count} local queue(s).";
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "Queue loading cancelled.";
        }
        catch (MsmqUnavailableException ex)
        {
            Queues = [];
            QueueGroups = [];
            WarningMessage = ex.Message;
            StatusMessage = "MSMQ is unavailable.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Failed to load queues: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private static ObservableCollection<QueueGroupViewModel> BuildQueueGroups(IEnumerable<QueueNodeViewModel> queues)
    {
        var queueList = queues.ToArray();

        QueueGroupViewModel[] groups =
        [
            new("Private Queues", queueList.Where(queue => queue.Type == QueueType.Private)),
            new("Public Queues", queueList.Where(queue => queue.Type == QueueType.Public)),
            new("System Queues", queueList.Where(queue => queue.Type is QueueType.System or QueueType.Journal or QueueType.DeadLetter)),
            new("Other Queues", queueList.Where(queue => queue.Type == QueueType.Unknown))
        ];

        return new ObservableCollection<QueueGroupViewModel>(groups.Where(group => group.Count > 0));
    }
}
