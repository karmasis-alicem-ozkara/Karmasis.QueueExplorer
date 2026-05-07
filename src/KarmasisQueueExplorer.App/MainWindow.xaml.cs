using KarmasisQueueExplorer.App.ViewModels;
using System.Windows;

namespace KarmasisQueueExplorer.App;

/// <summary>
/// Interaction logic for MainWindow.xaml
/// </summary>
public partial class MainWindow : Window
{
    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;

        Loaded += async (_, _) => await viewModel.LoadLocalQueuesCommand.ExecuteAsync(null);
    }
}
