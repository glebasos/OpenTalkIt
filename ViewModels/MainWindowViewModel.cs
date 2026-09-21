using System.ComponentModel;
using OpenTalkIt.Models;
using OpenTalkIt.Services;
using TiSpeech;

namespace OpenTalkIt.ViewModels;

public partial class MainWindowViewModel : ViewModelBase
{
    public PersonalityControlViewModel PersonalityVM { get; }
    public ParameterControlViewModel ParameterVM { get; }
    public TalkControlViewModel TalkVM { get; }

    public TiSpeechClient Engine { get; } = new();

    /// <summary>
    /// Whether the speech engine host came up successfully. On macOS/Linux
    /// this is currently always false — TiSpeech.Client guards non-Windows
    /// platforms itself and raises <see cref="TiSpeechClient.Error"/> with an
    /// explanatory message (native engine reconstruction in progress) instead
    /// of throwing or silently succeeding. The UI must reflect this rather
    /// than presenting Talk/Export as if they work; see
    /// <see cref="TalkControlViewModel.EngineUnavailableReason"/>.
    /// </summary>
    public bool EngineAvailable { get; }

    /// <summary>
    /// The message from the engine's own <see cref="TiSpeechClient.Error"/>
    /// event, captured around the <see cref="TiSpeechClient.Open"/> call
    /// below. Null when <see cref="EngineAvailable"/> is true. We subscribe
    /// to Error *before* calling Open() because TiSpeechClient raises it
    /// synchronously from inside Open() on failure (host missing, unsupported
    /// platform, etc.) — subscribing after the call would miss it.
    /// </summary>
    public string? EngineErrorMessage { get; private set; }

    public MainWindowViewModel() : this(null) { }

    public MainWindowViewModel(IExportService? exportService)
    {
        Engine.Error += (_, message) => EngineErrorMessage = message;
        EngineAvailable = Engine.Open(TiLanguageFlags.English | TiLanguageFlags.Spanish);

        PersonalityVM = new PersonalityControlViewModel();
        ParameterVM   = new ParameterControlViewModel();
        TalkVM = new TalkControlViewModel(Engine, () => new TalkParameters(
            Pitch:        PersonalityVM.Pitch,
            Speed:        PersonalityVM.Speed,
            Personality:  PersonalityVM.SelectedPersonality,
            Language:     ParameterVM.Language,
            VocalEffort:  ParameterVM.VocalEffort,
            PitchQuality: ParameterVM.PitchQuality
        ), exportService, EngineAvailable, EngineErrorMessage);

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