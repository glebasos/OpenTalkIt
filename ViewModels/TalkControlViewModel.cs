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

    public TalkControlViewModel(
        TiSpeechClient engine,
        Func<TalkParameters> getParameters,
        IExportService? exportService = null)
    {
        _engine = engine;
        _getParameters = getParameters;
        _exportService = exportService;
    }

    [ObservableProperty] private string _text = string.Empty;
    [ObservableProperty] private bool _isSpeaking;
    [ObservableProperty] private bool _isExporting;

    // Completed by SpeakCompleted event or by Stop() so TalkAsync never leaks.
    private TaskCompletionSource? _speakTcs;

    [RelayCommand(CanExecute = nameof(CanTalk))]
    private async Task TalkAsync()
    {
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
        if (_exportService is null) return;

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

    private bool CanTalk() => !IsSpeaking && !IsExporting && !string.IsNullOrWhiteSpace(Text);
    private bool CanStop() => IsSpeaking && !IsExporting;
    private bool CanExport() => _exportService is not null && !IsSpeaking && !IsExporting && !string.IsNullOrWhiteSpace(Text);

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
