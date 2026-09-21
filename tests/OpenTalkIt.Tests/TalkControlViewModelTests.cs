using System;
using OpenTalkIt.Models;
using OpenTalkIt.Services;
using OpenTalkIt.ViewModels;
using TiSpeech;
using Xunit;

namespace OpenTalkIt.Tests;

/// <summary>
/// Regression tests for the engine-availability / WAV-export-availability
/// gating added while porting OpenTalkIt off Windows-only APIs.
///
/// IMPORTANT: every test here constructs its own <see cref="TiSpeechClient"/>
/// but never calls <see cref="TiSpeechClient.Open"/> on it. TalkControlViewModel's
/// CanTalk/CanExport/EngineUnavailableReason logic only reads the
/// `engineAvailable`/`engineErrorMessage` constructor arguments and never
/// touches the engine for those checks, so no TiSpeech.Host process (Windows
/// x86 or otherwise) is ever launched by this suite.
/// </summary>
public class TalkControlViewModelTests
{
    private static TalkParameters DefaultParams() => new(
        Pitch: 100,
        Speed: 150,
        Personality: null,
        Language: TiLanguage.English,
        PitchQuality: TiF0Style.Natural,
        VocalEffort: TiVoicingMode.Normal
    );

    private static TalkControlViewModel MakeVm(
        bool engineAvailable,
        string? engineErrorMessage = null,
        IExportService? exportService = null)
        => new(new TiSpeechClient(), DefaultParams, exportService, engineAvailable, engineErrorMessage);

    // ── "Windows-like" state: engine reported available ─────────────────────
    // Simulates a successful TiSpeechClient.Open() result without launching a
    // real host process — the VM only ever sees the boolean/message that
    // MainWindowViewModel would have passed down after a real Open() call.

    [Fact]
    public void EngineAvailable_ClearsUnavailableReason()
    {
        var vm = MakeVm(engineAvailable: true);

        Assert.True(vm.EngineAvailable);
        Assert.Null(vm.EngineUnavailableReason);
    }

    [Fact]
    public void EngineAvailable_TalkCommand_EnabledWhenTextPresentAndIdle()
    {
        var vm = MakeVm(engineAvailable: true);

        Assert.False(vm.TalkCommand.CanExecute(null)); // empty text
        vm.Text = "Hello, world!";
        Assert.True(vm.TalkCommand.CanExecute(null));
        Assert.False(vm.StopCommand.CanExecute(null)); // not speaking yet
    }

    [Fact]
    public void EngineAvailable_ExportCommand_MatchesWavRecorderPlatformSupport()
    {
        // Whether Export is actually enabled once the engine is up still
        // depends on WAV export's own platform support (WASAPI is
        // Windows-only) — assert against the real WavRecorder.IsSupported
        // value so this test is correct on whichever OS runs it, rather than
        // hardcoding an OS assumption.
        var vm = MakeVm(engineAvailable: true, exportService: new TestExportService());
        vm.Text = "Hello, world!";

        Assert.Equal(WavRecorder.IsSupported, vm.ExportCommand.CanExecute(null));
        Assert.Equal(WavRecorder.IsSupported, vm.IsExportSupported);
    }

    // ── Engine unavailable (the macOS/Linux case today) ──────────────────────

    [Fact]
    public void EngineUnavailable_DisablesTalkAndExport()
    {
        var vm = MakeVm(engineAvailable: false, exportService: new TestExportService());
        vm.Text = "Hello, world!";

        Assert.False(vm.EngineAvailable);
        Assert.False(vm.TalkCommand.CanExecute(null));
        Assert.False(vm.ExportCommand.CanExecute(null));
        // Stop is unaffected — it only cares about IsSpeaking/IsExporting,
        // both false here, so it stays disabled too, but for a different reason.
        Assert.False(vm.StopCommand.CanExecute(null));
    }

    [Fact]
    public void EngineUnavailable_SurfacesTiSpeechClientErrorMessageVerbatim()
    {
        // This is the message TiSpeechClient.Open() actually raises via its
        // Error event on non-Windows platforms today. The banner in
        // TalkControl.axaml binds directly to EngineUnavailableReason, so
        // this pins the exact end-user-visible text for that path.
        const string clientMessage =
            "The original TiSpeech engine has not yet been fully ported to macOS/Linux. " +
            "The supplied Windows DLLs cannot run natively. Native engine reconstruction is in progress.";

        var vm = MakeVm(engineAvailable: false, engineErrorMessage: clientMessage);

        Assert.Equal(clientMessage, vm.EngineUnavailableReason);
    }

    [Fact]
    public void EngineUnavailable_NoClientMessage_FallsBackToACapabilityDerivedReason()
    {
        // CHANGED while replacing the UI's OS-based gating with real capability
        // queries. This test previously pinned two hardcoded strings chosen by
        // OperatingSystem.IsWindows(). That branch guessed a *cause* from the
        // platform and got it wrong whenever the real cause was something else
        // — a Windows box missing only TIENG32.DLL, or a macOS box where the
        // user HAD built the native library. The fallback now names the backend
        // that answered and the capabilities it reported, which is true
        // everywhere. See TalkControlViewModel.EngineUnavailableReason.
        //
        // Defense-in-depth path is unchanged in spirit: if the backend fails
        // without raising an Error message, the VM still shows something
        // concrete instead of a blank/null reason.
        var vm = MakeVm(engineAvailable: false);

        var reason = vm.EngineUnavailableReason!;
        Assert.False(string.IsNullOrWhiteSpace(reason));
        Assert.Contains("no synthesis capability", reason, StringComparison.Ordinal);
        Assert.Contains("Talk and Export stay disabled", reason, StringComparison.Ordinal);
        // The message describes the backend, not the operating system.
        Assert.DoesNotContain("platform", reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ExportUnavailableReason_IsNullOnlyWhenWavRecorderSupportsCurrentPlatform()
    {
        var vm = MakeVm(engineAvailable: true);

        if (WavRecorder.IsSupported)
            Assert.Null(vm.ExportUnavailableReason);
        else
            Assert.False(string.IsNullOrWhiteSpace(vm.ExportUnavailableReason));
    }
}
