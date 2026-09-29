using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using NAudio.Wave;
using TiSpeech;

namespace OpenTalkIt.Services;

/// <summary>Plays the portable engine's unsigned 8-bit PCM through Windows audio.</summary>
public sealed class WindowsPcmPlayer : IPcmPlayer
{
    private readonly object _sync = new();
    private readonly Func<IWavePlayer> _createOutput;
    private IWavePlayer? _output;
    private TaskCompletionSource? _completion;
    private bool _paused, _disposed;

    public WindowsPcmPlayer() : this(() => new WaveOutEvent()) { }
    internal WindowsPcmPlayer(Func<IWavePlayer> createOutput) => _createOutput = createOutput;

    public string? UnavailableReason => OperatingSystem.IsWindows()
        ? null : "Windows audio playback is unavailable on this platform.";

    public async Task PlayAsync(byte[] samples, int sampleRate, Action started, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var stream = new MemoryStream(samples, writable: false);
        using var source = new RawSourceWaveStream(stream, new WaveFormat(sampleRate, 8, 1));
        using var output = _createOutput();
        var stopped = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        void OnStopped(object? sender, StoppedEventArgs args)
        {
            if (args.Exception is { } error) stopped.TrySetException(error);
            else stopped.TrySetResult();
        }
        output.PlaybackStopped += OnStopped;
        try
        {
            lock (_sync)
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                if (_output is not null) throw new InvalidOperationException("Audio is already playing.");
                cancellationToken.ThrowIfCancellationRequested();
                output.Init(source);
                _output = output;
                _completion = stopped;
                if (!_paused) output.Play();
            }
            using var registration = cancellationToken.Register(() =>
            {
                lock (_sync)
                {
                    if (ReferenceEquals(_output, output)) output.Stop();
                }
            });
            cancellationToken.ThrowIfCancellationRequested();
            started();
            await stopped.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
        }
        finally
        {
            lock (_sync)
            {
                if (ReferenceEquals(_output, output))
                {
                    _output = null;
                    _completion = null;
                    output.Stop();
                }
            }
            output.PlaybackStopped -= OnStopped;
        }
    }

    public void Pause()
    {
        lock (_sync) { _paused = true; _output?.Pause(); }
    }

    public void Resume()
    {
        lock (_sync) { _paused = false; _output?.Play(); }
    }

    public void Dispose()
    {
        lock (_sync)
        {
            _disposed = true;
            _output?.Stop();
            _completion?.TrySetResult();
        }
    }
}
