using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OpenTalkIt.Models;
using OpenTalkIt.Services;
using TiSpeech;

namespace OpenTalkIt.ViewModels;

public partial class TalkControlViewModel : ViewModelBase, IDisposable
{
    private readonly ITiSpeechBackend _engine;
    private readonly Func<TalkParameters> _getParameters;
    private readonly IExportService? _exportService;

    private readonly CancellationTokenSource _lifetime = new();

    /// <summary>Native PCM export works without playback or loopback capture.</summary>
    public bool IsExportSupported => (_engine as ITiPcmRenderer)?.CanRender == true || WavRecorder.IsSupported;
    public string? ExportUnavailableReason => IsExportSupported ? null
        : "This backend has no direct WAV export, and system audio capture is unavailable.";

    private readonly string? _engineErrorMessage;

    /// <summary>
    /// Whether the selected backend opened for audio playback.
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

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasStatusMessage))]
    private string? _statusMessage;
    public bool HasStatusMessage => !string.IsNullOrWhiteSpace(StatusMessage);

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
        IsExporting = true;
        StatusMessage = null;
        try
        {
            var path = await _exportService.PickSaveFileAsync(SuggestFileName());
            if (string.IsNullOrEmpty(path)) return;
            if (_engine is ITiPcmRenderer { CanRender: true } renderer)
            {
                ApplyParameters();
                var audio = await renderer.RenderAsync(Text, _lifetime.Token);
                if (!audio.IsSuccess)
                    throw new InvalidOperationException(audio.Message ?? audio.Status.Describe());
                // Write beside the destination and replace it only when the WAV
                // is complete. A synthesis or write failure preserves the old file.
                var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
                try
                {
                    using (var file = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write))
                        PcmWaveFile.Write(file, audio.Samples!, audio.SampleRate);
                    _lifetime.Token.ThrowIfCancellationRequested();
                    File.Move(temporary, path, overwrite: true);
                }
                finally { if (File.Exists(temporary)) File.Delete(temporary); }
            }
            else
            {
                using var recorder = new WavRecorder();
                recorder.Start(path, silentPlayback: true);
                await Task.Delay(150, _lifetime.Token);
                await SpeakAndWaitAsync();
                await Task.Delay(250, _lifetime.Token);
                await recorder.StopAsync();
                if (StatusMessage is not null) return;
            }
            var folder = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(folder)) _exportService.RememberFolder(folder);
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
        catch (Exception ex) { StatusMessage = ex.Message; }
        finally { IsExporting = false; }
    }

    private void ApplyParameters()
    {
        var p = _getParameters();
        _engine.SetPersonality(p.Personality?.Personality ?? TiPersonality.Male);
        _engine.SetPitch(p.Pitch);
        _engine.SetRate(p.Speed);
        _engine.SetLanguage(p.Language);
        _engine.SetVoicingMode(p.VocalEffort);
        _engine.SetF0Style(p.PitchQuality);
    }

    private async Task SpeakAndWaitAsync()
    {
        StatusMessage = null;
        ApplyParameters();
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        string? error = null;
        void OnCompleted(object? sender, EventArgs args) => completion.TrySetResult();
        void OnError(object? sender, string message) => error = message;
        _speakTcs = completion;
        _engine.SpeakCompleted += OnCompleted;
        _engine.Error += OnError;
        using var cancelled = _lifetime.Token.Register(() => completion.TrySetCanceled(_lifetime.Token));
        try
        {
            _engine.Speak(Text);
            await completion.Task;
            StatusMessage = error;
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
        catch (Exception ex) { StatusMessage = ex.Message; }
        finally
        {
            _engine.SpeakCompleted -= OnCompleted;
            _engine.Error -= OnError;
            if (ReferenceEquals(_speakTcs, completion)) _speakTcs = null;
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
    private bool CanExport() => (EngineAvailable || (_engine as ITiPcmRenderer)?.CanRender == true) && _exportService is not null && IsExportSupported && !IsSpeaking && !IsExporting && !string.IsNullOrWhiteSpace(Text);

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
    public void Dispose()
    {
        _lifetime.Cancel();
        _engine.Stop();
    }

}
