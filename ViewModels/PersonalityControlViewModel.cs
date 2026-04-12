using System;
using System.Collections.ObjectModel;
using System.Linq;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OpenTalkIt.Helpers;
using OpenTalkIt.Models;
using TiSpeech;

namespace OpenTalkIt.ViewModels;

public partial class PersonalityControlViewModel : ViewModelBase
{
    private static readonly string[] Palette =
    {
        "#FFB3D9", "#FF9E80", "#FFCB80", "#FFEE80",
        "#CCEE80", "#A3E0A3", "#80E0B3", "#80D9D9",
        "#80C9EE", "#80A3FF", "#A0A0FF", "#B39EE8",
        "#C99EE8", "#DE9EE8", "#E89ECF", "#FFA3A3",
        "#FFCA9E", "#FFD96B", "#C9E6B3", "#9EE8D9",
    };

    [ObservableProperty] private int _pitch = 100;

    [ObservableProperty] private int _speed = 150;

    [ObservableProperty] private PersonalityButtonModel? _selectedPersonality;

    public ObservableCollection<PersonalityButtonModel> Personalities { get; } = new();

    [RelayCommand]
    private void SelectPersonality(PersonalityButtonModel p)
    {
        foreach (var personality in Personalities)
            personality.IsSelected = personality == p;
        SelectedPersonality = p;
        Pitch = p.Preset.Pitch;
        Speed = p.Preset.Speed;
    }

    public PersonalityControlViewModel()
    {
        int i = 0;
        foreach (TiPersonality p in Enum.GetValues<TiPersonality>())
        {
            var brush = new SolidColorBrush(Color.Parse(Palette[i % Palette.Length]));
            Personalities.Add(new PersonalityButtonModel(p.GetDescription(), p, brush));
            i++;
        }

        SelectedPersonality = Personalities.First();
        SelectedPersonality.IsSelected = true;
    }
}