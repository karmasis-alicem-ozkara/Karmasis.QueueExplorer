using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace KarmasisQueueExplorer.App.ViewModels;

public sealed partial class QueueGroupViewModel(string name, IEnumerable<object> children) : ObservableObject
{
    public string Name { get; } = name;

    public ObservableCollection<object> Children { get; } = new(children);

    public int Count => Children.Sum(child => child is QueueGroupViewModel group ? group.Count : 1);

    public string DisplayName => $"{Name} ({Count})";
}
