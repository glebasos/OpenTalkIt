using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using OpenTalkIt.ViewModels;

namespace OpenTalkIt.Views.Controls;

public partial class ParameterControl : UserControl
{
    public ParameterControl()
    {
        InitializeComponent();
        //DataContext = new ParameterControlViewModel();
    }
}