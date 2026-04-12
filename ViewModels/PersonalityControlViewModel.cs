using System;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OpenTalkIt.Helpers;
using OpenTalkIt.Models;
using TiSpeech;

namespace OpenTalkIt.ViewModels;

public partial class PersonalityControlViewModel : ViewModelBase
{
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
        foreach (TiPersonality p in Enum.GetValues<TiPersonality>())
        {
            Personalities.Add(new PersonalityButtonModel(p.GetDescription(), p));
        }

        SelectedPersonality = Personalities.First();
        SelectedPersonality.IsSelected = true;
    }
}