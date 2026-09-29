using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using OpenTalkIt.Services;
using OpenTalkIt.ViewModels;
using OpenTalkIt.Views;

namespace OpenTalkIt;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = new MainWindow();
        }
        else if (ApplicationLifetime is IActivityApplicationLifetime activity)
        {
            // Android calls the factory again whenever the activity is
            // recreated. The view model, and the speech engine it opens, is
            // created once and outlives every view; the process owns it.
            MainView? current = null;
            var viewModel = new MainWindowViewModel(new TopLevelExportService(() => TopLevel.GetTopLevel(current)));
            activity.MainViewFactory = () => current = new MainView { DataContext = viewModel };
        }
        else if (ApplicationLifetime is ISingleViewApplicationLifetime singleView)
        {
            var view = new MainView();
            view.DataContext = new MainWindowViewModel(new TopLevelExportService(() => TopLevel.GetTopLevel(view)));
            singleView.MainView = view;
        }

        base.OnFrameworkInitializationCompleted();
    }
}
