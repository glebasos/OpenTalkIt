using System;
using System.Threading;
using System.Threading.Tasks;
using TiSpeech;

namespace OpenTalkIt.Browser;

/// <summary>
/// Web Audio playback. The browser resamples the engine's native rate, so the
/// voice and its speed are unchanged; pause suspends the whole AudioContext.
/// </summary>
public sealed class BrowserPcmPlayer : IPcmPlayer
{
    public string? UnavailableReason => BrowserInterop.AudioUnavailableReason();

    public async Task PlayAsync(byte[] samples, int sampleRate, Action started, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        BrowserInterop.LoadPcm(samples, sampleRate);
        var playing = BrowserInterop.Play();
        using var registration = cancellationToken.Register(BrowserInterop.Stop);
        started();
        await playing.ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
    }

    public void Pause() => BrowserInterop.Pause();
    public void Resume() => BrowserInterop.Resume();
    public void Dispose() => BrowserInterop.Stop();
}
