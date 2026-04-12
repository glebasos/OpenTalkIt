using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using OpenTalkIt.ViewModels;
using TiSpeech;

namespace OpenTalkIt.Views.Controls;

public partial class PersonalityControl : UserControl
{
    public PersonalityControl()
    {
        InitializeComponent();
        //DataContext = new PersonalityControlViewModel();
    }
}