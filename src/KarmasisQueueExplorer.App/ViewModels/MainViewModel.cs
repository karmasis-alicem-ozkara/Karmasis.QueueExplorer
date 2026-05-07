using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using KarmasisQueueExplorer.Core.Services;
using KarmasisQueueExplorer.Core.Models;

namespace KarmasisQueueExplorer.App.ViewModels;

public sealed partial class MainViewModel(IMsmqService msmqService, IMessageBodyFormatter? messageBodyFormatter = null) : ObservableObject
{
    private readonly IMessageBodyFormatter _messageBodyFormatter = messageBodyFormatter ?? new MessageBodyFormatter();
    private readonly SynchronizationContext? _synchronizationContext = SynchronizationContext.Current;
    private CancellationTokenSource? _messageRefreshCts;

    [ObservableProperty]
    private ObservableCollection<QueueNodeViewModel> _queues = [];

    [ObservableProperty]
    private ObservableCollection<QueueGroupViewModel> _queueGroups = [];

    [ObservableProperty]
    private string _targetMachineName = ".";

    [ObservableProperty]
    private ObservableCollection<MessageInfo> _messages = [];

    [ObservableProperty]
    private ObservableCollection<MessageInfo> _filteredMessages = [];

    [ObservableProperty]
    private string _messageFilterText = string.Empty;

    [ObservableProperty]
    private string _newMessageLabel = "KarmasisQueueExplorer Test Message";

    [ObservableProperty]
    private string _newMessageBody = "{ \"source\": \"KarmasisQueueExplorer\", \"type\": \"test\" }";

    [ObservableProperty]
    private MessageInfo? _selectedMessage;

    [ObservableProperty]
    private string _selectedBodyText = string.Empty;

    [ObservableProperty]
    private string _selectedBodyJson = string.Empty;

    [ObservableProperty]
    private string _selectedBodyXml = string.Empty;

    [ObservableProperty]
    private string _selectedBodyHex = string.Empty;

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

    [ObservableProperty]
    private bool _isLoadingMessages;

    [ObservableProperty]
    private bool _isAutoRefreshEnabled = true;

    public IReadOnlyList<int> AutoRefreshIntervalOptions { get; } = [1, 2, 5, 10, 30];

    [ObservableProperty]
    private int _autoRefreshIntervalSeconds = 2;

    [ObservableProperty]
    private DateTime? _lastMessageRefreshTime;

    public string AutoRefreshStatus => IsAutoRefreshEnabled
        ? $"Auto-refresh: On ({AutoRefreshIntervalSeconds}s)"
        : "Auto-refresh: Paused";

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
        _messageRefreshCts?.Cancel();
        Messages = [];
        SelectedMessage = null;

        if (value is not null)
        {
            StatusMessage = $"Selected queue: {value.Path}";
            _ = LoadMessagesAndStartAutoRefreshAsync(value);
        }
    }

    partial void OnSelectedMessageChanged(MessageInfo? value)
    {
        var bodyText = value?.BodyText ?? string.Empty;

        SelectedBodyText = bodyText;
        SelectedBodyJson = _messageBodyFormatter.FormatJson(bodyText);
        SelectedBodyXml = _messageBodyFormatter.FormatXml(bodyText);
        SelectedBodyHex = _messageBodyFormatter.FormatHex(bodyText);
    }

    partial void OnMessageFilterTextChanged(string value)
    {
        ApplyMessageFilter();
    }

    partial void OnIsAutoRefreshEnabledChanged(bool value)
    {
        OnPropertyChanged(nameof(AutoRefreshStatus));

        if (SelectedQueue is null)
        {
            return;
        }

        _messageRefreshCts?.Cancel();
        if (value)
        {
            _ = LoadMessagesAndStartAutoRefreshAsync(SelectedQueue);
        }
    }

    partial void OnAutoRefreshIntervalSecondsChanged(int value)
    {
        OnPropertyChanged(nameof(AutoRefreshStatus));

        if (SelectedQueue is null || !IsAutoRefreshEnabled)
        {
            return;
        }

        _messageRefreshCts?.Cancel();
        _ = LoadMessagesAndStartAutoRefreshAsync(SelectedQueue);
    }

    [RelayCommand]
    private async Task LoadLocalQueuesAsync(CancellationToken cancellationToken)
    {
        try
        {
            IsBusy = true;
            WarningMessage = string.Empty;
            StatusMessage = $"Loading MSMQ queues from {TargetMachineName}...";

            var queues = await msmqService.GetQueuesAsync(TargetMachineName, cancellationToken);
            Queues = new ObservableCollection<QueueNodeViewModel>(queues.Select(queue => new QueueNodeViewModel(queue)));
            QueueGroups = BuildQueueGroups(Queues);
            StatusMessage = Queues.Count == 0
                ? $"No MSMQ queues found on {TargetMachineName}"
                : $"Loaded {Queues.Count} queue(s) from {TargetMachineName}";
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

    [RelayCommand]
    private async Task RefreshMessagesAsync(CancellationToken cancellationToken)
    {
        if (SelectedQueue is null)
        {
            StatusMessage = "Select a queue before refreshing messages.";
            return;
        }

        await LoadMessagesAsync(SelectedQueue, cancellationToken);
    }

    [RelayCommand]
    private async Task SendMessageAsync(CancellationToken cancellationToken)
    {
        if (SelectedQueue is null)
        {
            StatusMessage = "Select a queue before sending a message.";
            return;
        }

        try
        {
            IsBusy = true;
            StatusMessage = $"Sending message to {SelectedQueue.Path}...";

            await msmqService.SendMessageAsync(SelectedQueue.Path, NewMessageLabel, NewMessageBody, cancellationToken);
            StatusMessage = $"Sent message to {SelectedQueue.Name}.";
            await LoadMessagesAsync(SelectedQueue, cancellationToken, silent: true);
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "Send message cancelled.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Failed to send message: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task LoadMessagesAndStartAutoRefreshAsync(QueueNodeViewModel queue)
    {
        var cts = new CancellationTokenSource();
        _messageRefreshCts = cts;

        await LoadMessagesAsync(queue, cts.Token);

        if (IsAutoRefreshEnabled)
        {
            _ = RunMessageAutoRefreshAsync(queue, cts.Token);
        }
    }

    private async Task RunMessageAutoRefreshAsync(QueueNodeViewModel queue, CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(Math.Max(1, AutoRefreshIntervalSeconds)));

        try
        {
            while (await timer.WaitForNextTickAsync(cancellationToken))
            {
                await LoadMessagesAsync(queue, cancellationToken, silent: true);
            }
        }
        catch (OperationCanceledException)
        {
            // Expected when queue selection changes or auto-refresh is disabled.
        }
    }

    private async Task LoadMessagesAsync(QueueNodeViewModel queue, CancellationToken cancellationToken, bool silent = false)
    {
        try
        {
            await RunOnUiThreadAsync(() =>
            {
                IsLoadingMessages = true;
                if (!silent)
                {
                    StatusMessage = $"Loading messages from {queue.Path}...";
                }
            });

            var messages = await msmqService.GetMessagesAsync(queue.Path, maxCount: 250, cancellationToken);

            await RunOnUiThreadAsync(() =>
            {
                var previousSelectedId = SelectedMessage?.Id;
                Messages = new ObservableCollection<MessageInfo>(messages);
                ApplyMessageFilter();
                SelectedMessage = FilteredMessages.FirstOrDefault(message => message.Id == previousSelectedId) ?? FilteredMessages.FirstOrDefault();
                LastMessageRefreshTime = DateTime.Now;
                StatusMessage = $"Loaded {Messages.Count} message(s) from {queue.Name}.";
            });
        }
        catch (OperationCanceledException)
        {
            // Ignore expected cancellation.
        }
        catch (Exception ex)
        {
            await RunOnUiThreadAsync(() => StatusMessage = $"Failed to load messages: {ex.Message}");
        }
        finally
        {
            await RunOnUiThreadAsync(() => IsLoadingMessages = false);
        }
    }

    private Task RunOnUiThreadAsync(Action action)
    {
        if (_synchronizationContext is null || SynchronizationContext.Current == _synchronizationContext)
        {
            action();
            return Task.CompletedTask;
        }

        var completion = new TaskCompletionSource();
        _synchronizationContext.Post(_ =>
        {
            try
            {
                action();
                completion.SetResult();
            }
            catch (Exception ex)
            {
                completion.SetException(ex);
            }
        }, null);

        return completion.Task;
    }

    private void ApplyMessageFilter()
    {
        var filter = MessageFilterText.Trim();
        var filtered = string.IsNullOrWhiteSpace(filter)
            ? Messages
            : Messages.Where(message =>
                message.Label.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
                message.BodyPreview.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
                message.BodyText.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
                message.Id.Contains(filter, StringComparison.OrdinalIgnoreCase));

        FilteredMessages = new ObservableCollection<MessageInfo>(filtered);

        if (SelectedMessage is not null && !FilteredMessages.Any(message => message.Id == SelectedMessage.Id))
        {
            SelectedMessage = FilteredMessages.FirstOrDefault();
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
