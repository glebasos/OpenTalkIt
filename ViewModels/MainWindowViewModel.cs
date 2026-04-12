using System.ComponentModel;
using OpenTalkIt.Models;
using TiSpeech;

namespace OpenTalkIt.ViewModels;

public partial class MainWindowViewModel : ViewModelBase
{
    public PersonalityControlViewModel PersonalityVM { get; }
    public ParameterControlViewModel ParameterVM { get; }
    public TalkControlViewModel TalkVM { get; }

    public TiSpeechEngine Engine { get; } = new();

    public MainWindowViewModel()
    {
        Engine.Open("E:\\Projects\\dotnet\\OpenTalkIt\\OpenTalkIt\\DLLs", TiLanguageFlags.English | TiLanguageFlags.Spanish);

        PersonalityVM = new PersonalityControlViewModel();
        ParameterVM   = new ParameterControlViewModel();
        TalkVM = new TalkControlViewModel(Engine, () => new TalkParameters(
            Pitch:        PersonalityVM.Pitch,
            Speed:        PersonalityVM.Speed,
            Personality:  PersonalityVM.SelectedPersonality,
            Language:     ParameterVM.Language,
            VocalEffort:  ParameterVM.VocalEffort,
            PitchQuality: ParameterVM.PitchQuality
        ));

        PersonalityVM.PropertyChanged += OnPersonalitySelectionChanged;
    }

    private void OnPersonalitySelectionChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(PersonalityControlViewModel.SelectedPersonality)) return;
        var preset = PersonalityVM.SelectedPersonality?.Preset;
        if (preset is null) return;
        ParameterVM.PitchQuality = preset.PitchQuality;
        ParameterVM.VocalEffort  = preset.VocalEffort;
    }
}