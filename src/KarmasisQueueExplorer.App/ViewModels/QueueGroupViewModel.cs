using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace KarmasisQueueExplorer.App.ViewModels;

public sealed partial class QueueGroupViewModel(string name, IEnumerable<QueueNodeViewModel> children) : ObservableObject
{
    public string Name { get; } = name;

    public ObservableCollection<QueueNodeViewModel> Children { get; } = new(children);

    public int Count => Children.Count;

    public string DisplayName => $"{Name} ({Count})";
}
