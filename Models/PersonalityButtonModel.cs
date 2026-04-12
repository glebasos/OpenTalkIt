using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using TiSpeech;

namespace OpenTalkIt.Models;

public partial class PersonalityButtonModel : ObservableObject
{
    [ObservableProperty] private bool _isSelected;

    public string Label { get; }
    public TiPersonality Personality { get; }
    public PersonalityPreset Preset { get; }
    public IBrush ButtonBackground { get; }

    public PersonalityButtonModel(string label, TiPersonality personality, IBrush buttonBackground)
    {
        Label = label;
        Personality = personality;
        Preset = PersonalityPresets.For(personality);
        ButtonBackground = buttonBackground;
    }
}
