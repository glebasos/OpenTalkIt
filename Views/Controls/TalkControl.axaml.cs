using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using OpenTalkIt.ViewModels;

namespace OpenTalkIt.Views.Controls;

public partial class TalkControl : UserControl
{
    public TalkControl()
    {
        InitializeComponent();
        //DataContext = new TalkControlViewModel();
    }
}