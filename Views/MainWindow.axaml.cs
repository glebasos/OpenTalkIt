using Avalonia.Controls;
using OpenTalkIt.Services;
using OpenTalkIt.ViewModels;

namespace OpenTalkIt.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        DataContext = new MainWindowViewModel(new MainWindowExportService(this));
    }
}
