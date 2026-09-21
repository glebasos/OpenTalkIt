using System;
using System.IO;
using OpenTalkIt.Services;
using Xunit;

namespace OpenTalkIt.Tests;

/// <summary>
/// Regression tests for the Windows-only guard added to <see cref="WavRecorder"/>.
/// WAV export uses NAudio's WASAPI loopback capture, which has no macOS/Linux
/// implementation, so <see cref="WavRecorder.Start"/> must fail loudly with a
/// clear exception on non-Windows rather than silently producing no audio.
/// These tests never actually record audio — on Windows that would require a
/// real default output device, which isn't guaranteed in CI, so the
/// Windows branch only asserts the capability flag.
/// </summary>
public class WavRecorderTests
{
    [Fact]
    public void IsSupported_MatchesOperatingSystemIsWindows()
    {
        Assert.Equal(OperatingSystem.IsWindows(), WavRecorder.IsSupported);
    }

    [Fact]
    public void Start_OnNonWindows_ThrowsPlatformNotSupportedExceptionWithExplanation()
    {
        if (OperatingSystem.IsWindows())
            return; // covered by IsSupported_MatchesOperatingSystemIsWindows instead.

        using var recorder = new WavRecorder();
        var path = Path.Combine(Path.GetTempPath(), $"opentalkit-test-{Guid.NewGuid():N}.wav");

        var ex = Assert.Throws<PlatformNotSupportedException>(() => recorder.Start(path));

        Assert.Contains("WASAPI", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.False(File.Exists(path)); // must not create a partial/empty file
    }

    [Fact]
    public void Dispose_IsSafeWithoutEverCallingStart()
    {
        // Guards against a regression where the Windows-only guard skips
        // Start() but Dispose() still assumes _capture/_device were assigned.
        using var recorder = new WavRecorder();
        recorder.Dispose(); // via `using`; explicit call here documents intent
    }
}
