using System;
using System.ComponentModel;
using OpenTalkIt.Models;
using OpenTalkIt.Services;
using TiSpeech;

namespace OpenTalkIt.ViewModels;

public partial class MainWindowViewModel : ViewModelBase, IDisposable
{
    public PersonalityControlViewModel PersonalityVM { get; }
    public ParameterControlViewModel ParameterVM { get; }
    public TalkControlViewModel TalkVM { get; }

    /// <summary>
    /// The speech backend that answered — the 32-bit SoftVoice host on Windows,
    /// or the portable native reconstruction elsewhere. Chosen by
    /// <see cref="SpeechBackendFactory"/>, which asks each candidate to open
    /// rather than inspecting the operating system.
    /// </summary>
    public ITiSpeechBackend Engine { get; }

    /// <summary>
    /// Letter-to-sound provider, always the native reconstruction. Independent
    /// of <see cref="Engine"/> on purpose: converting text to phonemes and
    /// synthesising audio are separate stages with separate availability, and
    /// today exactly one of them works.
    /// </summary>
    public ITiPhonemeProvider PhonemeProvider { get; }

    /// <summary>
    /// Whether the speech backend came up and will actually produce the original
    /// engine's audio. False on macOS/Linux today: the native reconstruction's
    /// phoneme-to-audio stage is not written yet, and no system voice is
    /// substituted for it. The UI reflects this rather than presenting
    /// Talk/Export as if they work; see
    /// <see cref="TalkControlViewModel.EngineUnavailableReason"/>.
    /// </summary>
    public bool EngineAvailable { get; }

    /// <summary>
    /// Everything the candidate backends reported while failing to open, joined.
    /// Null when <see cref="EngineAvailable"/> is true. Captured inside
    /// <see cref="SpeechBackendFactory.Create"/> because backends raise
    /// <c>Error</c> synchronously from inside <c>Open()</c>.
    /// </summary>
    public string? EngineErrorMessage { get; }

    public MainWindowViewModel() : this(null) { }

    public MainWindowViewModel(IExportService? exportService)
    {
        var selection = SpeechBackendFactory.Create(TiLanguageFlags.English | TiLanguageFlags.Spanish);

        Engine             = selection.Speech;
        PhonemeProvider    = selection.Phonemes;
        EngineAvailable    = selection.SpeechOpened;
        EngineErrorMessage = selection.SpeechError;

        PersonalityVM = new PersonalityControlViewModel();
        ParameterVM   = new ParameterControlViewModel();

        // The phoneme VM reads the text out of TalkVM, and TalkVM owns the
        // phoneme VM, so one of the two has to be handed a deferred reference.
        // A local captured by the closure keeps that knot in one place.
        TalkControlViewModel? talkVm = null;
        var phonemeVm = new PhonemeControlViewModel(
            PhonemeProvider,
            () => talkVm?.Text ?? string.Empty,
            () => ParameterVM.Language);

        talkVm = new TalkControlViewModel(Engine, () => new TalkParameters(
            Pitch:        PersonalityVM.Pitch,
            Speed:        PersonalityVM.Speed,
            Personality:  PersonalityVM.SelectedPersonality,
            Language:     ParameterVM.Language,
            VocalEffort:  ParameterVM.VocalEffort,
            PitchQuality: ParameterVM.PitchQuality
        ), exportService, EngineAvailable, EngineErrorMessage, phonemeVm);
        TalkVM = talkVm;

        PersonalityVM.PropertyChanged += OnPersonalitySelectionChanged;
        ParameterVM.PropertyChanged   += OnParameterChanged;
    }

    private void OnPersonalitySelectionChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(PersonalityControlViewModel.SelectedPersonality)) return;
        var preset = PersonalityVM.SelectedPersonality?.Preset;
        if (preset is null) return;
        ParameterVM.PitchQuality = preset.PitchQuality;
        ParameterVM.VocalEffort  = preset.VocalEffort;
    }

    private void OnParameterChanged(object? sender, PropertyChangedEventArgs e)
    {
        // Phonemes are language-specific, so a language switch invalidates a
        // readout that is still on screen.
        if (e.PropertyName == nameof(ParameterControlViewModel.Language))
            TalkVM.NotifyLanguageChanged();
    }
    public void Dispose()
    {
        PersonalityVM.PropertyChanged -= OnPersonalitySelectionChanged;
        ParameterVM.PropertyChanged -= OnParameterChanged;
        TalkVM.Dispose();
        Engine.Dispose();
        if (!ReferenceEquals(PhonemeProvider, Engine) && PhonemeProvider is IDisposable provider)
            provider.Dispose();
    }

}
