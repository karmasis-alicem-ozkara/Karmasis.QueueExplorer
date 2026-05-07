using CommunityToolkit.Mvvm.ComponentModel;
using KarmasisQueueExplorer.Core.Models;

namespace KarmasisQueueExplorer.App.ViewModels;

public sealed partial class QueueNodeViewModel(QueueInfo queue) : ObservableObject
{
    public string Name { get; } = queue.Name;

    public string DisplayName => MessageCount is null
        ? Name
        : $"{Name} ({MessageCount})";

    public string Path { get; } = queue.Path;

    public string MachineName { get; } = queue.MachineName;

    public QueueType Type { get; } = queue.Type;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DisplayName))]
    private int? _messageCount = queue.MessageCount;
}
