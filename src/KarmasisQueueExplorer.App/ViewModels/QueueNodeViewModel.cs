using CommunityToolkit.Mvvm.ComponentModel;
using KarmasisQueueExplorer.Core.Models;

namespace KarmasisQueueExplorer.App.ViewModels;

public sealed partial class QueueNodeViewModel(QueueInfo queue) : ObservableObject
{
    public string Name { get; } = queue.Name;

    public string DisplayName { get; } = queue.MessageCount is null
        ? queue.Name
        : $"{queue.Name} ({queue.MessageCount})";

    public string Path { get; } = queue.Path;

    public string MachineName { get; } = queue.MachineName;

    public QueueType Type { get; } = queue.Type;

    public int? MessageCount { get; } = queue.MessageCount;
}
