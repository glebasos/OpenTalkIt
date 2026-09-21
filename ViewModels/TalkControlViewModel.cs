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
    private readonly TiSpeechClient _engine;
    private readonly Func<TalkParameters> _getParameters;
    private readonly IExportService? _exportService;

    /// <summary>
    /// WAV export captures system audio via NAudio's WASAPI loopback, which is
    /// a Windows-only API (no macOS/Linux equivalent shipped by NAudio). This
    /// guards the Export command until a cross-platform capture path (or a
    /// direct-to-file export from the engine) exists — see OpenTalkIt README /
    /// port notes. It intentionally does not fall back to any other backend.
    /// </summary>
    public bool IsExportSupported { get; } = WavRecorder.IsSupported;

    /// <summary>Bindable reason shown as a tooltip when export is unavailable; null on Windows.</summary>
    public string? ExportUnavailableReason => IsExportSupported
        ? null
        : "WAV export uses Windows audio capture (WASAPI loopback) and isn't available on this platform yet.";

    private readonly string? _engineErrorMessage;

    /// <summary>
    /// Whether <see cref="TiSpeechClient.Open"/> succeeded. On Windows this
    /// can still be false if the TiSpeech.Host x86 process or the SoftVoice
    /// DLLs are missing. On macOS/Linux, TiSpeechClient itself guards
    /// unsupported platforms and reports why via its Error event (native
    /// engine reconstruction in progress — no Wine/emulation shim, no
    /// alternative voice backend), so this is always false there today. The
    /// UI must not claim speech works when this is false.
    /// </summary>
    public bool EngineAvailable { get; }

    /// <summary>
    /// Bindable reason shown when the engine isn't available; null when it is.
    /// Prefers the actual message TiSpeechClient raised via its Error event
    /// (e.g. "native engine reconstruction is in progress") over a guessed
    /// one, so the UI shows what the engine itself reported instead of a
    /// generic guess.
    /// </summary>
    public string? EngineUnavailableReason => EngineAvailable
        ? null
        : !string.IsNullOrWhiteSpace(_engineErrorMessage)
            ? _engineErrorMessage
            : OperatingSystem.IsWindows()
                ? "Speech engine failed to start. Make sure TIBASE32.DLL and TIENG32.DLL are in OpenTalkIt/DLLs (see README), then restart."
                : "Speech engine isn't available on this platform yet. A native (non-Windows) engine is under active development — see the project README for status.";

    public TalkControlViewModel(
        TiSpeechClient engine,
        Func<TalkParameters> getParameters,
        IExportService? exportService = null,
        bool engineAvailable = true,
        string? engineErrorMessage = null)
    {
        _engine = engine;
        _getParameters = getParameters;
        _exportService = exportService;
        EngineAvailable = engineAvailable;
        _engineErrorMessage = engineErrorMessage;
    }

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
    }
}
