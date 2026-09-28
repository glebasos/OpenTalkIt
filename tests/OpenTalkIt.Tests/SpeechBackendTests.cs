using System;
using OpenTalkIt.Models;
using OpenTalkIt.Services;
using OpenTalkIt.ViewModels;
using TiSpeech;
using Xunit;

namespace OpenTalkIt.Tests;

/// <summary>
/// Tests for the engine abstraction and for the capability-driven gating that
/// replaced the old <c>OperatingSystem.IsWindows()</c> checks in the UI.
///
/// The point of the change is that the UI now asks the selected backend what it
/// can do instead of asking what OS this is. These tests drive both answers
/// through <see cref="FakeSpeechBackend"/> so the gating is verified in both
/// directions on any machine, and separately pin the native backend's refusal
/// to claim speech it cannot deliver.
/// </summary>
public class SpeechBackendTests
{
    private static TalkParameters DefaultParams() => new(
        Pitch: 100,
        Speed: 150,
        Personality: null,
        Language: TiLanguage.English,
        PitchQuality: TiF0Style.Natural,
        VocalEffort: TiVoicingMode.Normal
    );

    private static TalkControlViewModel MakeVm(ITiSpeechBackend backend, bool engineAvailable, string? error = null)
        => new(backend, DefaultParams, new TestExportService(), engineAvailable, error);

    // ── Capability gating, both directions ───────────────────────────────────

    [Fact]
    public void BackendReportingSynthesis_EnablesTalk()
    {
        var backend = new FakeSpeechBackend
        {
            Capabilities = TiEngineCapabilities.Synthesis,
            OpenResult = true,
        };
        Assert.True(backend.Open());

        var vm = MakeVm(backend, engineAvailable: true);
        vm.Text = "Hello, world!";

        Assert.Equal(TiEngineCapabilities.Synthesis, vm.EngineCapabilities);
        Assert.True(vm.TalkCommand.CanExecute(null));
        Assert.Null(vm.EngineUnavailableReason);
    }

    [Fact]
    public void BackendReportingNoSynthesis_DisablesTalkAndExport()
    {
        var backend = new FakeSpeechBackend
        {
            Name = "capability-less backend",
            Capabilities = TiEngineCapabilities.TextToPhonemes,
        };

        var vm = MakeVm(backend, engineAvailable: false);
        vm.Text = "Hello, world!";

        Assert.False(vm.TalkCommand.CanExecute(null));
        Assert.False(vm.ExportCommand.CanExecute(null));
        Assert.Equal(TiEngineCapabilities.TextToPhonemes, vm.EngineCapabilities);
    }

    [Fact]
    public void UnavailableReason_PrefersTheMessageTheBackendActuallyRaised()
    {
        const string raised = "TiSpeech.Host.exe not found at: /somewhere/x86/TiSpeech.Host.exe";

        var vm = MakeVm(new FakeSpeechBackend { UnavailableReason = "a vaguer backend-level reason" },
                        engineAvailable: false, error: raised);

        Assert.Equal(raised, vm.EngineUnavailableReason);
    }

    [Fact]
    public void UnavailableReason_FallsBackToTheBackendsOwnReason()
    {
        const string backendReason =
            "Speech synthesis is not implemented in the native engine reconstruction yet.";

        var vm = MakeVm(new FakeSpeechBackend { UnavailableReason = backendReason }, engineAvailable: false);

        Assert.Equal(backendReason, vm.EngineUnavailableReason);
    }

    [Fact]
    public void UnavailableReason_LastResortIsDerivedFromCapabilities_NotFromTheOperatingSystem()
    {
        // Replaces the old OS-branched fallback. A backend that reports nothing
        // at all still gets a message naming itself and its capabilities, which
        // is true on every platform — the previous text guessed a cause from
        // the host OS and was wrong whenever the real cause was something else.
        var vm = MakeVm(new FakeSpeechBackend { Name = "silent backend" }, engineAvailable: false);

        var reason = vm.EngineUnavailableReason!;
        Assert.Contains("silent backend", reason, StringComparison.Ordinal);
        Assert.Contains("no synthesis capability", reason, StringComparison.Ordinal);
        Assert.DoesNotContain("platform", reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void EngineName_IsSurfacedForMessagesAndLogs()
    {
        var vm = MakeVm(new FakeSpeechBackend { Name = "some backend" }, engineAvailable: true);
        Assert.Equal("some backend", vm.EngineName);
    }

    // ── The native reconstruction backend ────────────────────────────────────

    [Fact]
    public void NativeBackend_NeverClaimsSynthesis()
    {
        using var backend = new NativeTiSpeechBackend();

        Assert.False(backend.Capabilities.HasFlag(TiEngineCapabilities.Synthesis));
        Assert.False(backend.IsOpen);
        Assert.False(backend.IsSpeaking);
    }

    [Fact]
    public void NativeBackend_OpenReportsPlaybackReadiness()
    {
        using var backend = new NativeTiSpeechBackend();
        string? raised = null;
        backend.Error += (_, message) => raised = message;

        var opened = backend.Open(TiLanguageFlags.English);

        Assert.Equal(opened, backend.IsOpen);
        Assert.Equal(opened, backend.Capabilities.HasFlag(TiEngineCapabilities.Synthesis));
        if (opened)
        {
            Assert.Null(raised);
            Assert.Null(backend.UnavailableReason);
        }
        else
        {
            Assert.False(string.IsNullOrWhiteSpace(raised));
            Assert.Equal(backend.UnavailableReason, raised);
        }
    }

    [Fact]
    public void NativeBackend_UnavailableReason_DistinguishesMissingLibraryFromMissingSynthesis()
    {
        using var backend = new NativeTiSpeechBackend();
        using var player = new SystemPcmPlayer();
        if (TiSpeechNative.Capabilities.HasFlag(TiEngineCapabilities.Synthesis))
            Assert.Equal(player.UnavailableReason, backend.UnavailableReason);
        else
            Assert.False(string.IsNullOrWhiteSpace(backend.UnavailableReason));
    }

    [Fact]
    public void NativeBackend_SpeakBeforeOpen_ReportsFailureAndProducesNoAudio()
    {
        using var backend = new NativeTiSpeechBackend();
        string? raised = null;
        var started = false;
        var completed = false;
        backend.Error += (_, message) => raised = message;
        backend.SpeakStarted += (_, _) => started = true;
        backend.SpeakCompleted += (_, _) => completed = true;

        backend.Speak("hello");

        Assert.False(started);                 // nothing ever started speaking
        Assert.True(completed);                // but an awaiting caller is released
        Assert.False(string.IsNullOrWhiteSpace(raised));
        Assert.Contains("Open", raised!, StringComparison.Ordinal);
        Assert.False(backend.IsSpeaking);
    }

    [Fact]
    public void NativeBackend_IsAlsoThePhonemeProvider()
    {
        // One object, two roles: the portable reconstruction is the only thing
        // that converts text to phonemes, on every platform, including Windows
        // where the pipe/host backend does the speaking.
        using var backend = new NativeTiSpeechBackend();
        Assert.IsAssignableFrom<ITiPhonemeProvider>(backend);
        Assert.IsAssignableFrom<ITiSpeechBackend>(backend);
    }

    // ── TiSpeechClient implements the same contract ──────────────────────────

    [Fact]
    public void TiSpeechClient_ImplementsTheBackendContractAndClaimsNothingBeforeOpening()
    {
        // Never opened here, so no TiSpeech.Host process is launched on any
        // platform — the same discipline as the original suite.
        using var client = new TiSpeechClient();

        Assert.IsAssignableFrom<ITiSpeechBackend>(client);
        Assert.False(client.IsOpen);
        Assert.Equal(TiEngineCapabilities.None, client.Capabilities);
        Assert.Null(client.UnavailableReason); // nothing has gone wrong yet either
        Assert.False(string.IsNullOrWhiteSpace(client.Name));
    }

    // ── Factory ──────────────────────────────────────────────────────────────

    [Fact]
    public void Factory_SelectsNativePlaybackWhenAvailable()
    {
        // Skipped on Windows on purpose: there the factory would start the real
        // 32-bit TiSpeech.Host child process, which this suite deliberately
        // never does. The Windows selection path is therefore NOT covered by
        // automated tests.
        if (OperatingSystem.IsWindows()) return;

        var selection = SpeechBackendFactory.Create(TiLanguageFlags.English);

        Assert.NotNull(selection.Speech);
        Assert.NotNull(selection.Phonemes);
        using var player = new SystemPcmPlayer();
        var expected = TiSpeechNative.Capabilities.HasFlag(TiEngineCapabilities.Synthesis)
                       && player.UnavailableReason is null;
        Assert.Equal(expected, selection.SpeechOpened);
        Assert.Equal(expected, selection.Speech.Capabilities.HasFlag(TiEngineCapabilities.Synthesis));
        if (expected) Assert.Null(selection.SpeechError);
        else Assert.False(string.IsNullOrWhiteSpace(selection.SpeechError));

        selection.Speech.Dispose();
    }

    [Fact]
    public void Factory_AlwaysPairsThePortablePhonemeProvider()
    {
        if (OperatingSystem.IsWindows()) return; // see above

        var selection = SpeechBackendFactory.Create(TiLanguageFlags.English);

        Assert.IsType<NativeTiSpeechBackend>(selection.Phonemes);
        // The provider's availability is the native library's answer, nothing else.
        Assert.Equal(
            TiSpeechNative.IsAvailable
                && TiSpeechNative.Capabilities.HasFlag(TiEngineCapabilities.TextToPhonemes)
                && TiSpeechNative.Languages != 0,
            selection.Phonemes.IsAvailable);

        selection.Speech.Dispose();
    }
}
