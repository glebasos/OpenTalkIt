using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OpenTalkIt.Models;
using OpenTalkIt.Services;
using TiSpeech;

namespace OpenTalkIt.ViewModels;

public partial class TalkControlViewModel : ViewModelBase
{
    private readonly ITiSpeechBackend _engine;
    private readonly Func<TalkParameters> _getParameters;
    private readonly IExportService? _exportService;

    /// <summary>
    /// WAV export captures system audio via NAudio's WASAPI loopback, which is
    /// a Windows-only API (no macOS/Linux equivalent shipped by NAudio). This
    /// guards the Export command until a cross-platform capture path (or a
    /// direct-to-file export from the engine) exists — see OpenTalkIt README /
    /// port notes. It intentionally does not fall back to any other backend.
    ///
    /// Unlike the speech gate below, this one legitimately stays an OS check:
    /// the missing thing here is a platform audio API, not an engine
    /// capability, so there is nothing to ask the backend about.
    /// </summary>
    public bool IsExportSupported { get; } = WavRecorder.IsSupported;

    /// <summary>Bindable reason shown as a tooltip when export is unavailable; null on Windows.</summary>
    public string? ExportUnavailableReason => IsExportSupported
        ? null
        : "WAV export uses Windows audio capture (WASAPI loopback) and isn't available on this platform yet.";

    private readonly string? _engineErrorMessage;

    /// <summary>
    /// Whether the selected backend actually opened — i.e. whether calling
    /// <see cref="ITiSpeechBackend.Speak"/> will produce the original engine's
    /// audio. On Windows this is the 32-bit SoftVoice host; on macOS/Linux it is
    /// false today because the portable reconstruction's phoneme-to-audio stage
    /// is not written yet (<c>tispeech_synthesize()</c> returns
    /// TISPEECH_E_NOTIMPL) and no substitute voice is used by design.
    ///
    /// The UI must not claim speech works when this is false.
    /// </summary>
    public bool EngineAvailable { get; }

    /// <summary>
    /// What the selected backend can really do, straight from
    /// <see cref="ITiSpeechBackend.Capabilities"/>. This is what the gates below
    /// consult instead of <c>OperatingSystem.IsWindows()</c>: the question is
    /// never "which OS is this?" but "can this backend synthesise?".
    /// </summary>
    public TiEngineCapabilities EngineCapabilities => _engine.Capabilities;

    /// <summary>Backend name, so messages and logs can say which one answered.</summary>
    public string EngineName => _engine.Name;

    /// <summary>
    /// Bindable reason shown when the engine isn't available; null when it is.
    ///
    /// Sourced entirely from what the backends themselves reported — the
    /// message captured around <see cref="ITiSpeechBackend.Open"/> first, then
    /// the backend's own <see cref="ITiSpeechBackend.UnavailableReason"/>, then
    /// a capability-derived fallback. There is deliberately no
    /// <c>OperatingSystem.IsWindows()</c> branch here any more: the old one
    /// guessed a cause from the platform, and guessed wrong whenever the real
    /// cause was something else (missing DLLs on Windows, a native library the
    /// user had built on macOS, ...).
    /// </summary>
    public string? EngineUnavailableReason => EngineAvailable
        ? null
        : !string.IsNullOrWhiteSpace(_engineErrorMessage)
            ? _engineErrorMessage
            : !string.IsNullOrWhiteSpace(_engine.UnavailableReason)
                ? _engine.UnavailableReason
                : $"The {_engine.Name} backend is loaded but reports no synthesis capability " +
                  $"({DescribeCapabilities(EngineCapabilities)}), so Talk and Export stay disabled.";

    /// <summary>Phoneme readout — the one cross-platform feature that is genuinely backed by working code.</summary>
    public PhonemeControlViewModel? PhonemeVM { get; }

    public bool HasPhonemeFeature => PhonemeVM is not null;

    public TalkControlViewModel(
        ITiSpeechBackend engine,
        Func<TalkParameters> getParameters,
        IExportService? exportService = null,
        bool engineAvailable = true,
        string? engineErrorMessage = null,
        PhonemeControlViewModel? phonemeVm = null)
    {
        _engine = engine;
        _getParameters = getParameters;
        _exportService = exportService;
        EngineAvailable = engineAvailable;
        _engineErrorMessage = engineErrorMessage;
        PhonemeVM = phonemeVm;
    }

    private static string DescribeCapabilities(TiEngineCapabilities capabilities) =>
        capabilities == TiEngineCapabilities.None ? "no capabilities" : capabilities.ToString();

    [ObservableProperty] private string _text = string.Empty;
    [ObservableProperty] private bool _isSpeaking;
    [ObservableProperty] private bool _isExporting;

    // Completed by SpeakCompleted event or by Stop() so TalkAsync never leaks.
    private TaskCompletionSource? _speakTcs;

    [RelayCommand(CanExecute = nameof(CanTalk))]
    private async Task TalkAsync()
    {
        if (!EngineAvailable) return;

        IsSpeaking = true;
        try
        {
            await SpeakAndWaitAsync();
        }
        finally
        {
            IsSpeaking = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanExport))]
    private async Task ExportAsync()
    {
        if (_exportService is null || !IsExportSupported) return;

        var path = await _exportService.PickSaveFileAsync(SuggestFileName());
        if (string.IsNullOrEmpty(path)) return;

        IsExporting = true;
        try
        {
            using var recorder = new WavRecorder();
            recorder.Start(path, silentPlayback: true);

            // Let WASAPI loopback prime before playback so we don't clip the attack.
            await Task.Delay(150);

            await SpeakAndWaitAsync();

            // Small tail so the final syllable isn't truncated.
            await Task.Delay(250);
            await recorder.StopAsync();

            var folder = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(folder))
                _exportService.RememberFolder(folder);
        }
        finally
        {
            IsExporting = false;
        }
    }

    private async Task SpeakAndWaitAsync()
    {
        var p = _getParameters();

        _engine.SetPersonality(p.Personality?.Personality ?? TiPersonality.Male);
        _engine.SetPitch(p.Pitch);
        _engine.SetRate(p.Speed);
        _engine.SetLanguage(p.Language);
        _engine.SetVoicingMode(p.VocalEffort);
        _engine.SetF0Style(p.PitchQuality);

        _speakTcs = new TaskCompletionSource();
        _engine.SpeakCompleted += OnCompleted;
        _engine.Speak(Text);

        await _speakTcs.Task;

        void OnCompleted(object? s, EventArgs e)
        {
            _engine.SpeakCompleted -= OnCompleted;
            _speakTcs?.TrySetResult();
        }
    }

    [RelayCommand(CanExecute = nameof(CanStop))]
    private void Stop()
    {
        _engine.Stop();
        _speakTcs?.TrySetResult();
        _speakTcs = null;
        IsSpeaking = false;
    }

    private bool CanTalk() => EngineAvailable && !IsSpeaking && !IsExporting && !string.IsNullOrWhiteSpace(Text);
    private bool CanStop() => IsSpeaking && !IsExporting;
    private bool CanExport() => EngineAvailable && _exportService is not null && IsExportSupported && !IsSpeaking && !IsExporting && !string.IsNullOrWhiteSpace(Text);

    private string SuggestFileName()
    {
        var snippet = new string(Text.Take(24).Where(c => char.IsLetterOrDigit(c) || c == ' ').ToArray()).Trim();
        if (string.IsNullOrEmpty(snippet)) snippet = "talkit";
        return snippet + ".wav";
    }

    partial void OnIsSpeakingChanged(bool value)
    {
        TalkCommand.NotifyCanExecuteChanged();
        StopCommand.NotifyCanExecuteChanged();
        ExportCommand.NotifyCanExecuteChanged();
    }

    partial void OnIsExportingChanged(bool value)
    {
        TalkCommand.NotifyCanExecuteChanged();
        StopCommand.NotifyCanExecuteChanged();
        ExportCommand.NotifyCanExecuteChanged();
    }

    partial void OnTextChanged(string value)
    {
        TalkCommand.NotifyCanExecuteChanged();
        ExportCommand.NotifyCanExecuteChanged();
        // The phoneme readout belongs to the previous text; drop it.
        PhonemeVM?.NotifyInputChanged();
    }

    /// <summary>
    /// Called by <see cref="MainWindowViewModel"/> when the language radio
    /// changes: the same text converts to different phonemes per language, so a
    /// stale readout would be wrong rather than merely old.
    /// </summary>
    public void NotifyLanguageChanged() => PhonemeVM?.NotifyInputChanged();
}
