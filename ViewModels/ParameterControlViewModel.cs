using CommunityToolkit.Mvvm.ComponentModel;
using TiSpeech;

namespace OpenTalkIt.ViewModels;

public partial class ParameterControlViewModel : ViewModelBase
{
    [ObservableProperty] private TiF0Style _pitchQuality = TiF0Style.Natural;
    [ObservableProperty] private TiVoicingMode _vocalEffort = TiVoicingMode.Normal;
    [ObservableProperty] private TiLanguage _language = TiLanguage.English;

}