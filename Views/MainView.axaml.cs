using Avalonia.Controls;

namespace OpenTalkIt.Views;

/// <summary>
/// The whole app UI. Hosted by <see cref="MainWindow"/> on desktop and set as
/// the activity's content on Android; either way the host supplies the
/// <see cref="ViewModels.MainWindowViewModel"/> it inherits as DataContext.
/// </summary>
public partial class MainView : UserControl
{
    // Below this the desktop layout no longer fits (the window's MinWidth is 720).
    private const double NarrowWidth = 600;

    public MainView()
    {
        InitializeComponent();
#if ANDROID
        // The animated shader redraws every frame, which costs battery on a
        // phone; the plain background colour stands in for it.
        Root.Children.Remove(Clouds);
#endif
    }

    protected override void OnSizeChanged(SizeChangedEventArgs e)
    {
        base.OnSizeChanged(e);
        Classes.Set("narrow", e.NewSize.Width < NarrowWidth);
    }
}
