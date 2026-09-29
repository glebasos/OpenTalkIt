using System;
using System.Threading;
using System.Threading.Tasks;
using NAudio.Wave;
using OpenTalkIt.Services;
using Xunit;

namespace OpenTalkIt.Tests;

public class WindowsPcmPlayerTests
{
    private sealed class Output : IWavePlayer
    {
        public event EventHandler<StoppedEventArgs>? PlaybackStopped;
        public IWaveProvider? Source;
        public PlaybackState PlaybackState { get; private set; }
        public float Volume { get; set; }
        public WaveFormat OutputWaveFormat => Source!.WaveFormat;
        public bool Disposed;
        public void Init(IWaveProvider source) => Source = source;
        public void Play() => PlaybackState = PlaybackState.Playing;
        public void Pause() => PlaybackState = PlaybackState.Paused;
        public void Stop()
        {
            if (PlaybackState != PlaybackState.Stopped) Finish();
        }
        public void Finish(Exception? error = null)
        {
            PlaybackState = PlaybackState.Stopped;
            PlaybackStopped?.Invoke(this, new StoppedEventArgs(error));
        }
        public void Dispose() => Disposed = true;
    }

    [Fact]
    public async Task PreservesPcmAndSupportsPauseResumeAndCompletion()
    {
        var output = new Output();
        using var player = new WindowsPcmPlayer(() => output);
        var started = false;
        byte[] pcm = [0, 128, 255];
        var task = player.PlayAsync(pcm, 11025, () => started = true, CancellationToken.None);
        Assert.True(started);
        Assert.False(task.IsCompleted);
        Assert.Equal(11025, output.Source!.WaveFormat.SampleRate);
        Assert.Equal(8, output.Source.WaveFormat.BitsPerSample);
        Assert.Equal(1, output.Source.WaveFormat.Channels);
        var actual = new byte[3];
        Assert.Equal(3, output.Source.Read(actual, 0, actual.Length));
        Assert.Equal(pcm, actual);
        player.Pause();
        Assert.Equal(PlaybackState.Paused, output.PlaybackState);
        player.Resume();
        Assert.Equal(PlaybackState.Playing, output.PlaybackState);
        output.Finish();
        await task.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.True(output.Disposed);
    }

    [Fact]
    public async Task CancellationStopsAndDisposesTheOutput()
    {
        var output = new Output();
        using var player = new WindowsPcmPlayer(() => output);
        using var cancel = new CancellationTokenSource();
        var task = player.PlayAsync([128], 11025, () => { }, cancel.Token);
        cancel.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task.WaitAsync(TimeSpan.FromSeconds(3)));
        Assert.Equal(PlaybackState.Stopped, output.PlaybackState);
        Assert.True(output.Disposed);
    }

    [Fact]
    public async Task DeviceFailureIsReportedAndResourcesAreReleased()
    {
        var output = new Output();
        using var player = new WindowsPcmPlayer(() => output);
        var task = player.PlayAsync([128], 11025, () => { }, CancellationToken.None);
        output.Finish(new InvalidOperationException("Device lost"));
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => task.WaitAsync(TimeSpan.FromSeconds(3)));
        Assert.Equal("Device lost", error.Message);
        Assert.True(output.Disposed);
    }

    [Fact]
    public async Task DisposingBeforePausedPlaybackStartsStillCompletes()
    {
        var output = new Output();
        using var player = new WindowsPcmPlayer(() => output);
        player.Pause();
        var task = player.PlayAsync([128], 11025, () => { }, CancellationToken.None);
        Assert.Equal(PlaybackState.Stopped, output.PlaybackState);
        Assert.False(task.IsCompleted);
        // WaveOut does not raise PlaybackStopped if it never started.
        player.Dispose();
        await task.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.True(output.Disposed);
    }
}
