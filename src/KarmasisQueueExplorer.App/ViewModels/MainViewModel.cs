using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using KarmasisQueueExplorer.Core.Services;
using KarmasisQueueExplorer.Core.Models;

namespace KarmasisQueueExplorer.App.ViewModels;

public sealed partial class MainViewModel(
    IMsmqService msmqService,
    IMessageBodyFormatter? messageBodyFormatter = null,
    IMessageExportService? messageExportService = null,
    IDialogService? dialogService = null,
    IConnectionProfileStore? connectionProfileStore = null) : ObservableObject
{
    private readonly IMessageBodyFormatter _messageBodyFormatter = messageBodyFormatter ?? new MessageBodyFormatter();
    private readonly IMessageExportService _messageExportService = messageExportService ?? new MessageExportService();
    private readonly IDialogService _dialogService = dialogService ?? new NoOpDialogService();
    private readonly IConnectionProfileStore _connectionProfileStore = connectionProfileStore ?? new NoOpConnectionProfileStore();
    private readonly SynchronizationContext? _synchronizationContext = SynchronizationContext.Current;
    private CancellationTokenSource? _messageRefreshCts;

    /// <summary>
    /// Prevents <see cref="OnSelectedSavedProfileChanged"/> from triggering a queue
    /// load while <see cref="InitializeAsync"/> is pre-selecting the startup profile.
    /// </summary>
    private bool _suppressAutoConnect;

    [ObservableProperty]
    private ObservableCollection<QueueNodeViewModel> _queues = [];

    [ObservableProperty]
    private ObservableCollection<QueueGroupViewModel> _queueGroups = [];

    [ObservableProperty]
    private string _targetMachineName = ".";

    [ObservableProperty]
    private ObservableCollection<ConnectionProfile> _savedProfiles = [];

    [ObservableProperty]
    private ConnectionProfile? _selectedSavedProfile;

    /// <summary>Display name typed by the user when saving a new profile.</summary>
    [ObservableProperty]
    private string _newProfileDisplayName = string.Empty;

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
    private string _targetQueuePath = string.Empty;

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

    /// <summary>Inverse of <see cref="IsBusy"/>; drives UI element <c>IsEnabled</c> bindings.</summary>
    public bool IsNotBusy => !IsBusy;

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

    partial void OnIsBusyChanged(bool value)
    {
        OnPropertyChanged(nameof(IsNotBusy));
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
        RefreshCommandStates();

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
        RefreshCommandStates();
    }

    partial void OnTargetQueuePathChanged(string value)
    {
        CopySelectedMessageCommand.NotifyCanExecuteChanged();
        MoveSelectedMessageCommand.NotifyCanExecuteChanged();
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

    partial void OnSelectedSavedProfileChanged(ConnectionProfile? value)
    {
        if (value is not null)
        {
            TargetMachineName = value.MachineName;

            // Auto-connect: selecting a profile immediately loads queues for that machine.
            // Suppressed during InitializeAsync to avoid an unwanted connect on startup.
            if (!_suppressAutoConnect)
            {
                _ = LoadLocalQueuesCommand.ExecuteAsync(null);
            }
        }

        RemoveSelectedProfileCommand.NotifyCanExecuteChanged();
    }

    /// <summary>
    /// Loads saved connection profiles from the store on application startup.
    /// Should be called once by the host after the main window is created.
    /// Profile pre-selection does NOT trigger an automatic queue load — the user
    /// initiates the first connect explicitly or via the profile picker thereafter.
    /// </summary>
    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        var profiles = await _connectionProfileStore.LoadProfilesAsync(cancellationToken);
        SavedProfiles = new ObservableCollection<ConnectionProfile>(profiles);

        // Suppress auto-connect while we restore the previously selected profile.
        _suppressAutoConnect = true;
        try
        {
            SelectedSavedProfile = SavedProfiles.FirstOrDefault(p => p.MachineName == TargetMachineName)
                ?? SavedProfiles.FirstOrDefault();
        }
        finally
        {
            _suppressAutoConnect = false;
        }
    }

    [RelayCommand]
    private async Task SaveCurrentProfileAsync(CancellationToken cancellationToken)
    {
        var machineName = TargetMachineName.Trim();
        if (string.IsNullOrWhiteSpace(machineName))
        {
            StatusMessage = "Enter a machine name before saving a profile.";
            return;
        }

        var displayName = string.IsNullOrWhiteSpace(NewProfileDisplayName)
            ? machineName
            : NewProfileDisplayName.Trim();

        // Replace an existing profile for the same machine name, or add a new one.
        var existing = SavedProfiles.FirstOrDefault(p => p.MachineName.Equals(machineName, StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
        {
            var index = SavedProfiles.IndexOf(existing);
            SavedProfiles[index] = new ConnectionProfile(displayName, machineName);
        }
        else
        {
            SavedProfiles.Add(new ConnectionProfile(displayName, machineName));
        }

        await _connectionProfileStore.SaveProfilesAsync([.. SavedProfiles], cancellationToken);
        SelectedSavedProfile = SavedProfiles.FirstOrDefault(p => p.MachineName.Equals(machineName, StringComparison.OrdinalIgnoreCase));
        NewProfileDisplayName = string.Empty;
        StatusMessage = $"Saved connection profile '{displayName}'.";
    }

    [RelayCommand(CanExecute = nameof(HasSelectedProfile))]
    private async Task RemoveSelectedProfileAsync(CancellationToken cancellationToken)
    {
        if (SelectedSavedProfile is null)
        {
            return;
        }

        var removed = SelectedSavedProfile;
        SavedProfiles.Remove(removed);
        SelectedSavedProfile = SavedProfiles.FirstOrDefault();
        await _connectionProfileStore.SaveProfilesAsync([.. SavedProfiles], cancellationToken);
        StatusMessage = $"Removed connection profile '{removed.DisplayName}'.";
    }

    private bool HasSelectedProfile() => SelectedSavedProfile is not null;

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

    [RelayCommand(CanExecute = nameof(HasSelectedQueue))]
    private async Task RefreshMessagesAsync(CancellationToken cancellationToken)
    {
        if (SelectedQueue is null)
        {
            StatusMessage = "Select a queue before refreshing messages.";
            return;
        }

        await LoadMessagesAsync(SelectedQueue, cancellationToken);
    }

    [RelayCommand(CanExecute = nameof(HasSelectedQueue))]
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

    [RelayCommand(CanExecute = nameof(HasSelectedMessage))]
    private async Task ExportSelectedMessageAsync(CancellationToken cancellationToken)
    {
        if (SelectedMessage is null)
        {
            StatusMessage = "Select a message before exporting.";
            return;
        }

        try
        {
            IsBusy = true;
            var filePath = await _messageExportService.ExportMessageAsync(SelectedMessage, SelectedQueue?.Path, cancellationToken);
            StatusMessage = $"Exported selected message to {filePath}";
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "Message export cancelled.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Failed to export message: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand(CanExecute = nameof(HasSelectedMessageAndQueue))]
    private async Task DeleteSelectedMessageAsync(CancellationToken cancellationToken)
    {
        if (SelectedQueue is null || SelectedMessage is null)
        {
            StatusMessage = "Select a queue message before deleting.";
            return;
        }

        var confirmed = await _dialogService.ConfirmAsync(
            "Delete selected message",
            $"Delete message '{SelectedMessage.Label}' from {SelectedQueue.Path}? This operation cannot be undone.",
            cancellationToken);

        if (!confirmed)
        {
            StatusMessage = "Delete cancelled.";
            return;
        }

        try
        {
            IsBusy = true;
            await msmqService.DeleteMessageAsync(SelectedQueue.Path, SelectedMessage.Id, cancellationToken);
            StatusMessage = $"Deleted message {SelectedMessage.Label}.";
            await LoadMessagesAsync(SelectedQueue, cancellationToken, silent: true);
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "Delete cancelled.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Failed to delete message: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanCopyOrMoveSelectedMessage))]
    private async Task CopySelectedMessageAsync(CancellationToken cancellationToken)
    {
        if (SelectedMessage is null)
        {
            StatusMessage = "Select a message before copying.";
            return;
        }

        if (string.IsNullOrWhiteSpace(TargetQueuePath))
        {
            StatusMessage = "Enter target queue path before copying.";
            return;
        }

        try
        {
            IsBusy = true;
            await msmqService.CopyMessageAsync(TargetQueuePath.Trim(), SelectedMessage, cancellationToken);
            StatusMessage = $"Copied message {SelectedMessage.Label} to {TargetQueuePath.Trim()}.";
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "Copy cancelled.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Failed to copy message: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanCopyOrMoveSelectedMessage))]
    private async Task MoveSelectedMessageAsync(CancellationToken cancellationToken)
    {
        if (SelectedQueue is null || SelectedMessage is null)
        {
            StatusMessage = "Select a queue message before moving.";
            return;
        }

        if (string.IsNullOrWhiteSpace(TargetQueuePath))
        {
            StatusMessage = "Enter target queue path before moving.";
            return;
        }

        var targetQueuePath = TargetQueuePath.Trim();
        var confirmed = await _dialogService.ConfirmAsync(
            "Move selected message",
            $"Move message '{SelectedMessage.Label}' from {SelectedQueue.Path} to {targetQueuePath}? Source message will be deleted after copy.",
            cancellationToken);

        if (!confirmed)
        {
            StatusMessage = "Move cancelled.";
            return;
        }

        try
        {
            IsBusy = true;
            await msmqService.CopyMessageAsync(targetQueuePath, SelectedMessage, cancellationToken);
            await msmqService.DeleteMessageAsync(SelectedQueue.Path, SelectedMessage.Id, cancellationToken);
            StatusMessage = $"Moved message {SelectedMessage.Label} to {targetQueuePath}.";
            await LoadMessagesAsync(SelectedQueue, cancellationToken, silent: true);
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "Move cancelled.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Failed to move message: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand(CanExecute = nameof(HasSelectedQueue))]
    private async Task PurgeSelectedQueueAsync(CancellationToken cancellationToken)
    {
        if (SelectedQueue is null)
        {
            StatusMessage = "Select a queue before purging.";
            return;
        }

        var confirmed = await _dialogService.ConfirmAsync(
            "Purge selected queue",
            $"Delete ALL messages from {SelectedQueue.Path}? This operation cannot be undone.",
            cancellationToken);

        if (!confirmed)
        {
            StatusMessage = "Purge cancelled.";
            return;
        }

        try
        {
            IsBusy = true;
            await msmqService.PurgeQueueAsync(SelectedQueue.Path, cancellationToken);
            Messages = [];
            FilteredMessages = [];
            SelectedMessage = null;
            StatusMessage = $"Purged queue {SelectedQueue.Name}.";
            await LoadMessagesAsync(SelectedQueue, cancellationToken, silent: true);
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "Purge cancelled.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Failed to purge queue: {ex.Message}";
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
                queue.MessageCount = Messages.Count;
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

    private bool HasSelectedQueue() => SelectedQueue is not null;

    private bool HasSelectedMessage() => SelectedMessage is not null;

    private bool HasSelectedMessageAndQueue() => SelectedQueue is not null && SelectedMessage is not null;

    private bool CanCopyOrMoveSelectedMessage() => SelectedMessage is not null && !string.IsNullOrWhiteSpace(TargetQueuePath);

    private void RefreshCommandStates()
    {
        RefreshMessagesCommand.NotifyCanExecuteChanged();
        SendMessageCommand.NotifyCanExecuteChanged();
        ExportSelectedMessageCommand.NotifyCanExecuteChanged();
        DeleteSelectedMessageCommand.NotifyCanExecuteChanged();
        CopySelectedMessageCommand.NotifyCanExecuteChanged();
        MoveSelectedMessageCommand.NotifyCanExecuteChanged();
        PurgeSelectedQueueCommand.NotifyCanExecuteChanged();
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

    private sealed class NoOpDialogService : IDialogService
    {
        public Task<bool> ConfirmAsync(string title, string message, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(false);
        }
    }

    private sealed class NoOpConnectionProfileStore : IConnectionProfileStore
    {
        public Task<IReadOnlyList<ConnectionProfile>> LoadProfilesAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<ConnectionProfile>>([]);

        public Task SaveProfilesAsync(IReadOnlyList<ConnectionProfile> profiles, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }
}
