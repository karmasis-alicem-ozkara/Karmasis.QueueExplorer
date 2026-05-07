using System.Windows;
using System.Windows.Controls;

namespace KarmasisQueueExplorer.App.Behaviors;

public static class TreeViewSelectionBehavior
{
    public static readonly DependencyProperty SelectedItemProperty = DependencyProperty.RegisterAttached(
        "SelectedItem",
        typeof(object),
        typeof(TreeViewSelectionBehavior),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnSelectedItemChanged));

    public static object? GetSelectedItem(DependencyObject obj)
    {
        return obj.GetValue(SelectedItemProperty);
    }

    public static void SetSelectedItem(DependencyObject obj, object? value)
    {
        obj.SetValue(SelectedItemProperty, value);
    }

    private static void OnSelectedItemChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not TreeView treeView)
        {
            return;
        }

        treeView.SelectedItemChanged -= TreeViewOnSelectedItemChanged;
        treeView.SelectedItemChanged += TreeViewOnSelectedItemChanged;
    }

    private static void TreeViewOnSelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (sender is TreeView treeView)
        {
            SetSelectedItem(treeView, e.NewValue);
        }
    }
}
