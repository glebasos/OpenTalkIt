using System;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OpenTalkIt.Models;
using TiSpeech;

namespace OpenTalkIt.ViewModels;

public partial class TalkControlViewModel : ViewModelBase
{
    private readonly TiSpeechEngine _engine;
    private readonly Func<TalkParameters> _getParameters;

    public TalkControlViewModel(TiSpeechEngine engine, Func<TalkParameters> getParameters)
    {
        _engine = engine;
        _getParameters = getParameters;
    }
    
    [ObservableProperty] private string _text = string.Empty;
    [ObservableProperty] private bool _isSpeaking;
    
    [RelayCommand(CanExecute = nameof(CanTalk))]
    private async Task TalkAsync()
    {
        IsSpeaking = true;                                                                                                                                                                                                                                                                                            
        var p = _getParameters();

        _engine.SetPersonality(p.Personality?.Personality ?? TiPersonality.Male); 
        _engine.SetPitch(p.Pitch);
        _engine.SetRate(p.Speed);
        _engine.SetLanguage(p.Language);
        _engine.SetVoicingMode(p.VocalEffort);
        _engine.SetF0Style(p.PitchQuality);

        var tcs = new TaskCompletionSource();
        _engine.SpeakCompleted += OnCompleted;
        _engine.Speak(Text);

        await tcs.Task;

        void OnCompleted(object? s, EventArgs e)
        {
            _engine.SpeakCompleted -= OnCompleted;
            tcs.SetResult();
            IsSpeaking = false;
        }

    }

    [RelayCommand(CanExecute = nameof(CanStop))]
    private void Stop()
    {
        _engine.Stop();
        IsSpeaking = false;
    }

    private bool CanTalk() => !IsSpeaking && !string.IsNullOrWhiteSpace(Text);
    private bool CanStop() => IsSpeaking;
    
    partial void OnIsSpeakingChanged(bool value)                                                                                                                                                                                                                                                                      
    {                                                                                                                                                                                                                                                                                                                 
        TalkCommand.NotifyCanExecuteChanged();
        StopCommand.NotifyCanExecuteChanged();
    }

    partial void OnTextChanged(string value)
    {
        TalkCommand.NotifyCanExecuteChanged();
    }



}