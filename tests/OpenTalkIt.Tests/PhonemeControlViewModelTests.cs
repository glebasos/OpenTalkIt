using System;
using OpenTalkIt.ViewModels;
using TiSpeech;
using Xunit;

namespace OpenTalkIt.Tests;

/// <summary>
/// Tests for the one genuinely new cross-platform feature: the phoneme readout.
///
/// It is backed by working code — the reconstructed SoftVoice letter-to-sound
/// rules, compiled as portable C — so unlike Talk/Export it really is enabled
/// on macOS and Linux. These tests pin the gating (capability bits and language
/// data, never the OS), the two distinct unavailable states, and the rule that
/// a failed conversion clears the readout rather than leaving something stale
/// or partial on screen.
/// </summary>
public class PhonemeControlViewModelTests
{
    private static PhonemeControlViewModel MakeVm(
        ITiPhonemeProvider provider,
        string text = "",
        TiLanguage language = TiLanguage.English)
        => new(provider, () => text, () => language);

    // ── Available: the macOS/Linux story this work is shipping ───────────────

    [Fact]
    public void Available_CommandNeedsText()
    {
        var vm = MakeVm(new FakePhonemeProvider(), text: "");
        Assert.True(vm.IsAvailable);
        Assert.Null(vm.UnavailableReason);
        Assert.False(vm.ShowPhonemesCommand.CanExecute(null));

        var withText = MakeVm(new FakePhonemeProvider(), text: "hello");
        Assert.True(withText.ShowPhonemesCommand.CanExecute(null));
    }

    [Fact]
    public void Available_ShowPhonemes_PublishesTheConvertedString()
    {
        var provider = new FakePhonemeProvider
        {
            Conversion = (_, _) => TiPhonemeResult.Success("DHAX KWIHK BROWN FAAKS"),
        };
        var vm = MakeVm(provider, text: "the quick brown fox");

        vm.ShowPhonemesCommand.Execute(null);

        Assert.Equal("DHAX KWIHK BROWN FAAKS", vm.Phonemes);
        Assert.True(vm.HasPhonemes);
        Assert.Null(vm.StatusMessage);
        Assert.False(vm.HasStatusMessage);
        Assert.Equal(1, provider.CallCount);
    }

    [Fact]
    public void Available_EmptyResultSaysSoInsteadOfShowingABlankPill()
    {
        var provider = new FakePhonemeProvider { Conversion = (_, _) => TiPhonemeResult.Success("") };
        var vm = MakeVm(provider, text: "...");

        vm.ShowPhonemesCommand.Execute(null);

        Assert.False(vm.HasPhonemes);
        Assert.True(vm.HasStatusMessage);
    }

    [Fact]
    public void Available_ReportsWhatTheBuildCanConvert()
    {
        var vm = MakeVm(new FakePhonemeProvider
        {
            SupportedLanguages = TiLanguageFlags.English | TiLanguageFlags.Spanish,
        });

        Assert.Equal("English, Spanish", vm.SupportedLanguages);
        Assert.False(string.IsNullOrWhiteSpace(vm.BuildInfo));
    }

    // ── Missing native library ───────────────────────────────────────────────

    [Fact]
    public void MissingLibrary_FeatureIsOffAndExplainsItself()
    {
        var vm = MakeVm(FakePhonemeProvider.MissingLibrary(), text: "hello");

        Assert.False(vm.IsAvailable);
        Assert.False(vm.ShowPhonemesCommand.CanExecute(null));
        Assert.True(vm.ShowUnavailableNotice);
        Assert.Contains("TIENG32.DLL", vm.UnavailableReason!, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("none", vm.SupportedLanguages);
    }

    // ── Library present, no language data compiled in ────────────────────────

    [Fact]
    public void MissingLanguageData_IsADistinctStateWithItsOwnMessage()
    {
        var provider = FakePhonemeProvider.MissingLanguageData();
        var vm = MakeVm(provider, text: "hello");

        Assert.False(vm.IsAvailable);
        Assert.False(vm.ShowPhonemesCommand.CanExecute(null));
        Assert.True(vm.ShowUnavailableNotice);
        // Distinguishable from the missing-library message: this one says the
        // library IS loaded, so the user does not go looking for the wrong fix.
        Assert.Contains("loaded but was built without language data", vm.UnavailableReason!, StringComparison.Ordinal);
        Assert.Equal("none", vm.SupportedLanguages);
        // The library's own self-description survives even in this state.
        Assert.Contains("no language data", vm.BuildInfo!, StringComparison.Ordinal);
    }

    // ── Per-request language mismatch ────────────────────────────────────────

    [Fact]
    public void LanguageWithoutRuleData_ExplainsRatherThanConverting()
    {
        // English-only build, Spanish selected in the UI.
        var provider = new FakePhonemeProvider { SupportedLanguages = TiLanguageFlags.English };
        var vm = MakeVm(provider, text: "hola", language: TiLanguage.Spanish);

        // Still "available" — the feature works, just not for this language.
        Assert.True(vm.IsAvailable);
        Assert.True(vm.ShowPhonemesCommand.CanExecute(null));

        vm.ShowPhonemesCommand.Execute(null);

        Assert.Null(vm.Phonemes);
        Assert.False(vm.HasPhonemes);
        Assert.Contains("Spanish", vm.StatusMessage!, StringComparison.Ordinal);
        Assert.Contains("English", vm.StatusMessage!, StringComparison.Ordinal);
        // Short-circuited in the view model; the provider was never asked.
        Assert.Equal(0, provider.CallCount);
    }

    // ── Failure never leaves a stale or invented readout ─────────────────────

    [Fact]
    public void FailedConversion_ClearsTheReadout()
    {
        var succeed = true;
        var provider = new FakePhonemeProvider
        {
            Conversion = (_, _) => succeed
                ? TiPhonemeResult.Success("HHEHLOW")
                : TiPhonemeResult.Failure(TiStatus.BufferFull, "output buffer too small"),
        };
        var vm = MakeVm(provider, text: "hello");

        vm.ShowPhonemesCommand.Execute(null);
        Assert.Equal("HHEHLOW", vm.Phonemes);

        succeed = false;
        vm.ShowPhonemesCommand.Execute(null);

        Assert.Null(vm.Phonemes);
        Assert.False(vm.HasPhonemes);
        Assert.Equal("output buffer too small", vm.StatusMessage);
    }

    [Fact]
    public void NotifyInputChanged_DropsAReadoutThatNoLongerMatchesTheText()
    {
        var vm = MakeVm(new FakePhonemeProvider { Conversion = (_, _) => TiPhonemeResult.Success("HHEHLOW") },
                        text: "hello");

        vm.ShowPhonemesCommand.Execute(null);
        Assert.True(vm.HasPhonemes);

        vm.NotifyInputChanged();

        Assert.Null(vm.Phonemes);
        Assert.Null(vm.StatusMessage);
        Assert.False(vm.HasPhonemes);
        Assert.False(vm.HasStatusMessage);
    }

    [Fact]
    public void ShowUnavailableNotice_IsFalseWhenTheFeatureWorks()
    {
        Assert.False(MakeVm(new FakePhonemeProvider()).ShowUnavailableNotice);
    }

    // ── The real provider, wired the way the app wires it ────────────────────

    [Fact]
    public void RealNativeProvider_GatesOnCapabilitiesNotOnTheOperatingSystem()
    {
        ITiPhonemeProvider provider = new NativeTiSpeechBackend();
        var vm = MakeVm(provider, text: "hello");

        // Whatever this machine has, the view model's availability must equal
        // the native library's own answer — never OperatingSystem.IsWindows().
        var expected = TiSpeechNative.IsAvailable
                       && TiSpeechNative.Capabilities.HasFlag(TiEngineCapabilities.TextToPhonemes)
                       && TiSpeechNative.Languages != 0;

        Assert.Equal(expected, vm.IsAvailable);
        Assert.Equal(expected, vm.ShowPhonemesCommand.CanExecute(null));
        Assert.Equal(!expected, vm.ShowUnavailableNotice);
    }
}
