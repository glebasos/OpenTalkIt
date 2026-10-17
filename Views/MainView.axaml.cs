using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Platform;
using Avalonia.Input;
using Avalonia.Threading;

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
    // Below this (a phone in landscape) the frame gives up its wide margins.
    private const double ShortHeight = 600;

    private IInputPane? _inputPane;

    public MainView()
    {
        InitializeComponent();
#if ANDROID || BROWSER || NO_SHADER
        // The animated shader redraws every frame, which costs battery on a
        // phone and keeps a browser tab rendering nonstop (and NO_SHADER
        // desktop builds opt out of it); the plain background colour stands
        // in for it.
        Root.Children.Remove(Clouds);
#endif
        Scroller.GotFocus += (_, e) => BringIntoViewLater(e.Source as Control);
    }

    protected override void OnSizeChanged(SizeChangedEventArgs e)
    {
        base.OnSizeChanged(e);
        Classes.Set("narrow", e.NewSize.Width < NarrowWidth);
        Classes.Set("short", e.NewSize.Height < ShortHeight);
        UpdateKeyboardInset();
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _inputPane = TopLevel.GetTopLevel(this)?.InputPane;
        if (_inputPane is not null) _inputPane.StateChanged += OnInputPaneStateChanged;
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        if (_inputPane is not null) _inputPane.StateChanged -= OnInputPaneStateChanged;
        _inputPane = null;
        base.OnDetachedFromVisualTree(e);
    }

    private void OnInputPaneStateChanged(object? sender, InputPaneStateEventArgs e)
    {
        UpdateKeyboardInset();
        BringIntoViewLater(TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement() as Control);
    }

    /// <summary>
    /// Lifts the UI above the on-screen keyboard by however much the keyboard
    /// still covers it. Where the platform has already shrunk the view to
    /// make room the overlap is zero, so this never counts the keyboard twice.
    /// </summary>
    private void UpdateKeyboardInset()
    {
        double overlap = 0;
        if (_inputPane is { State: InputPaneState.Open } pane
            && TopLevel.GetTopLevel(this) is { } top
            && this.TranslatePoint(new Point(0, Bounds.Height), top) is { } bottom)
        {
            overlap = Math.Clamp(bottom.Y - pane.OccludedRect.Top, 0, Bounds.Height);
        }
        Root.Margin = new Thickness(0, 0, 0, overlap);
    }

    // Waits for the layout pass that follows a keyboard or focus change, so the
    // scroll offset is computed against the viewport the user will actually see.
    private static void BringIntoViewLater(Control? control)
    {
        if (control is not TextBox) return;
        Dispatcher.UIThread.Post(control.BringIntoView, DispatcherPriority.Background);
    }
}
