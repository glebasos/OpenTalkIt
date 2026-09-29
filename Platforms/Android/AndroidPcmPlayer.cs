using System;
using System.Threading;
using System.Threading.Tasks;
using Android.Media;
using TiSpeech;

namespace OpenTalkIt.Android;

/// <summary>
/// Plays the engine's unsigned 8-bit mono PCM through <see cref="AudioTrack"/>,
/// which takes that format as is. Samples are streamed with non-blocking
/// writes, so pause, resume and cancellation never wait on the audio thread.
/// </summary>
public sealed class AndroidPcmPlayer : IPcmPlayer
{
    private readonly Lock _sync = new();
    private AudioTrack? _track;
    private bool _paused, _disposed;

    public string? UnavailableReason => null;

    public async Task PlayAsync(byte[] samples, int sampleRate, Action started, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var minimum = AudioTrack.GetMinBufferSize(sampleRate, ChannelOut.Mono, Encoding.Pcm8bit);
        var track = new AudioTrack.Builder()
            .SetAudioAttributes(new AudioAttributes.Builder()
                .SetUsage(AudioUsageKind.Media)!
                .SetContentType(AudioContentType.Speech)!
                .Build()!)
            .SetAudioFormat(new AudioFormat.Builder()
                .SetEncoding(Encoding.Pcm8bit)!
                .SetSampleRate(sampleRate)!
                .SetChannelMask(ChannelOut.Mono)!
                .Build()!)
            .SetBufferSizeInBytes(Math.Max(minimum, sampleRate / 2))
            .SetTransferMode(AudioTrackMode.Stream)
            .Build();
        try
        {
            lock (_sync)
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                if (_track is not null) throw new InvalidOperationException("Audio is already playing.");
                _track = track;
                track.Play();
                if (_paused) track.Pause();
            }
            started();

            int written = 0;
            while (written < samples.Length)
            {
                cancellationToken.ThrowIfCancellationRequested();
                int n = track.Write(samples, written, samples.Length - written, WriteMode.NonBlocking);
                if (n < 0) throw new InvalidOperationException($"Audio playback failed (AudioTrack error {n}).");
                written += n;
                if (written < samples.Length) await Task.Delay(20, cancellationToken).ConfigureAwait(false);
            }
            // Stream mode has no end-of-data callback; wait until the head has
            // played the last frame (one byte per frame here).
            while (track.PlaybackHeadPosition < samples.Length)
                await Task.Delay(20, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            lock (_sync) { if (ReferenceEquals(_track, track)) _track = null; }
            try { track.Stop(); } catch (Java.Lang.IllegalStateException) { }
            track.Release();
            track.Dispose();
        }
    }

    private void SetPaused(bool paused)
    {
        lock (_sync)
        {
            _paused = paused;
            if (_track is null) return;
            if (paused) _track.Pause(); else _track.Play();
        }
    }
    public void Pause() => SetPaused(true);
    public void Resume() => SetPaused(false);

    public void Dispose()
    {
        lock (_sync)
        {
            _disposed = true;
            try { _track?.Stop(); } catch (Java.Lang.IllegalStateException) { }
        }
    }
}
