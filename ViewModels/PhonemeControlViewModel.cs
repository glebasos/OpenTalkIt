using System;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TiSpeech;

namespace OpenTalkIt.ViewModels;

/// <summary>
/// Shows the phoneme string the reconstructed SoftVoice letter-to-sound rules
/// produce for the text in the box.
///
/// This is the one thing the native reconstruction really can do everywhere
/// today — it runs the original engine's rule tables as portable C, on macOS
/// and Linux as well as Windows, with no Wine, no emulator and no substitute
/// voice. It is gated on <c>tispeech_capabilities()</c> and
/// <c>tispeech_languages()</c> rather than on the host OS, so it lights up
/// exactly when the library backing it can actually answer.
///
/// It does NOT imply that speech works. Synthesis is a separate, unimplemented
/// stage; Talk and Export stay disabled regardless of what this shows.
/// </summary>
public partial class PhonemeControlViewModel : ViewModelBase
{
    private readonly ITiPhonemeProvider _provider;
    private readonly Func<string> _getText;
    private readonly Func<TiLanguage> _getLanguage;

    /// <summary>
    /// Whether the converter can answer at all: the native library loaded, it
    /// reports TISPEECH_CAP_TEXT_TO_PHONEMES, and it has rule data for at least
    /// one language. All three are separate failure modes with separate
    /// messages — see <see cref="UnavailableReason"/>.
    /// </summary>
    public bool IsAvailable { get; }

    /// <summary>
    /// Why conversion is unavailable, or null when it is available. Missing
    /// library is the common case and is phrased as a normal state a
    /// contributor can fix, not as an error.
    /// </summary>
    public string? UnavailableReason { get; }

    /// <summary>
    /// The probe trail behind <see cref="UnavailableReason"/> (every path the
    /// loader tried). Shown as a tooltip, not in the panel: it is a dozen
    /// paths long and would bury the actionable sentence.
    /// </summary>
    public string? UnavailableDetail { get; }

    /// <summary>The native library's own description of itself, for a tooltip.</summary>
    public string? BuildInfo { get; }

    /// <summary>Languages this build can convert, e.g. "English", or "none".</summary>
    public string SupportedLanguages { get; }

    [ObservableProperty] private string? _phonemes;
    [ObservableProperty] private string? _statusMessage;

    public PhonemeControlViewModel(
        ITiPhonemeProvider provider,
        Func<string> getText,
        Func<TiLanguage> getLanguage)
    {
        _provider = provider;
        _getText = getText;
        _getLanguage = getLanguage;

        IsAvailable        = provider.IsAvailable;
        UnavailableReason  = provider.UnavailableReason;
        UnavailableDetail  = provider.UnavailableDetail;
        BuildInfo          = provider.BuildInfo;
        SupportedLanguages = TiSpeechNative.DescribeLanguages(provider.SupportedLanguages);
    }

    public bool HasPhonemes     => !string.IsNullOrEmpty(Phonemes);
    public bool HasStatusMessage => !string.IsNullOrEmpty(StatusMessage);

    /// <summary>
    /// True when the feature is unavailable and we have something to say about
    /// it — drives the explanatory panel that replaces the readout.
    /// </summary>
    public bool ShowUnavailableNotice => !IsAvailable && !string.IsNullOrWhiteSpace(UnavailableReason);

    [RelayCommand(CanExecute = nameof(CanShowPhonemes))]
    private void ShowPhonemes()
    {
        var text = _getText();
        var language = _getLanguage();

        // Checked here as well as in CanExecute because a language change can
        // move a previously convertible request out of range.
        if (!_provider.SupportedLanguages.HasFlag((TiLanguageFlags)(uint)language))
        {
            Phonemes = null;
            StatusMessage =
                $"This build of the native library has no {language} letter-to-sound data " +
                $"(it has: {SupportedLanguages}). The rule tables come from the original language DLLs.";
            RaiseReadoutChanged();
            return;
        }

        var result = _provider.TextToPhonemes(language, text);
        if (result.IsSuccess)
        {
            Phonemes = result.Phonemes;
            StatusMessage = string.IsNullOrWhiteSpace(result.Phonemes)
                ? "The rules produced no phonemes for that text."
                : null;
        }
        else
        {
            // Never show a partial or invented phoneme string: a failed
            // conversion clears the readout and explains itself instead.
            Phonemes = null;
            StatusMessage = result.Message ?? result.Status.Describe();
        }

        RaiseReadoutChanged();
    }

    private bool CanShowPhonemes() => IsAvailable && !string.IsNullOrWhiteSpace(_getText());

    /// <summary>
    /// Called by <see cref="TalkControlViewModel"/> when the text or language
    /// changes. Clears the readout, because a phoneme string shown next to text
    /// it no longer belongs to is worse than no phoneme string at all.
    /// </summary>
    public void NotifyInputChanged()
    {
        Phonemes = null;
        StatusMessage = null;
        RaiseReadoutChanged();
        ShowPhonemesCommand.NotifyCanExecuteChanged();
    }

    private void RaiseReadoutChanged()
    {
        OnPropertyChanged(nameof(HasPhonemes));
        OnPropertyChanged(nameof(HasStatusMessage));
    }
}
