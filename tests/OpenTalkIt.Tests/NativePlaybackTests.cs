using System;
using System.Collections.Concurrent;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using OpenTalkIt.Models;
using OpenTalkIt.Services;
using OpenTalkIt.ViewModels;
using TiSpeech;
using Xunit;

namespace OpenTalkIt.Tests;

public class NativePlaybackTests
{
    private sealed class Synth : INativePcmSynthesizer
    {
        public TiEngineCapabilities Capabilities => TiEngineCapabilities.Synthesis | TiEngineCapabilities.TextToPhonemes;
        public TiLanguageFlags Languages => TiLanguageFlags.English;
        public string? UnavailableReason => null;
        public Func<string, TiVoiceOptions, TiSynthesisResult> Generate { get; set; } =
            (_, _) => new(TiStatus.Ok, [128, 140, 120], 11025);
        public TiSynthesisResult Render(TiLanguage language, string text, TiVoiceOptions options, TiUserDictionary? dictionary) => Generate(text, options);
    }

    private sealed class Player : IPcmPlayer
    {
        public string? UnavailableReason { get; init; }
        public readonly TaskCompletionSource Started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public readonly TaskCompletionSource Finish = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public readonly TaskCompletionSource Cancelled = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public readonly ConcurrentQueue<byte[]> Played = new();
        public bool Paused;
        public async Task PlayAsync(byte[] samples, int rate, Action started, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            Played.Enqueue(samples);
            started();
            Started.TrySetResult();
            try { await Finish.Task.WaitAsync(token); }
            catch (OperationCanceledException) { Cancelled.TrySetResult(); throw; }
        }
        public void Pause() => Paused = true;
        public void Resume() => Paused = false;
        public void Dispose() { }
    }

    [Fact]
    public async Task InterruptedSynthesisCannotPlayOrCompleteItsReplacement()
    {
        var firstEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var releaseFirst = new ManualResetEventSlim();
        var captured = new ConcurrentQueue<TiVoiceOptions>();
        var synth = new Synth { Generate = (text, options) =>
        {
            captured.Enqueue(options);
            if (text == "first")
            {
                firstEntered.TrySetResult();
                if (!releaseFirst.Wait(TimeSpan.FromSeconds(5))) throw new TimeoutException();
            }
            return new(TiStatus.Ok, [(byte)(text == "first" ? 1 : 2)], 11025);
        }};
        var player = new Player();
        using var backend = new NativeTiSpeechBackend(player, synth);
        Assert.True(backend.Open());
        int completions = 0;
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        backend.SpeakCompleted += (_, _) => { Interlocked.Increment(ref completions); done.TrySetResult(); };
        backend.SetPitch(100);
        backend.Speak("first");
        await firstEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        backend.SetPitch(250);
        backend.Speak("second");
        await player.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        releaseFirst.Set();
        Assert.True(backend.IsSpeaking);
        Assert.Equal(0, completions);
        player.Finish.TrySetResult();
        await done.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(1, completions);
        Assert.Equal(new byte[] { 2 }, Assert.Single(player.Played));
        Assert.Equal(100, captured.ToArray()[0].Pitch);
        Assert.Equal(250, captured.ToArray()[1].Pitch);
    }

    [Fact]
    public async Task StopCancelsPlaybackAndCompletesOnce()
    {
        var player = new Player();
        using var backend = new NativeTiSpeechBackend(player, new Synth());
        Assert.True(backend.Open());
        int completed = 0;
        backend.SpeakCompleted += (_, _) => Interlocked.Increment(ref completed);
        backend.Speak("hello");
        await player.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        backend.Pause();
        Assert.True(player.Paused);
        backend.Resume();
        Assert.False(player.Paused);
        backend.Stop();
        await player.Cancelled.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.False(backend.IsSpeaking);
        Assert.Equal(1, completed);
        backend.Stop();
        Assert.Equal(1, completed);
    }

    [Fact]
    public async Task SynthesisFailureIsVisibleAndNeverStartsPlayback()
    {
        var synth = new Synth { Generate = (_, _) => TiSynthesisResult.Failure(TiStatus.BadParam, "Too long") };
        var player = new Player();
        using var backend = new NativeTiSpeechBackend(player, synth);
        Assert.True(backend.Open());
        using var vm = new TalkControlViewModel(backend, Parameters, engineAvailable: true) { Text = "hello" };
        await vm.TalkCommand.ExecuteAsync(null).WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal("Too long", vm.StatusMessage);
        Assert.False(vm.IsSpeaking);
        Assert.Empty(player.Played);
    }

    private static TalkParameters Parameters() => new(100, 150, null, TiLanguage.English,
        TiF0Style.Natural, TiVoicingMode.Normal);

    private sealed class ExportService(string path) : IExportService
    {
        public string? Folder;
        public Task<string?> PickSaveFileAsync(string name) => Task.FromResult<string?>(path);
        public void RememberFolder(string folder) => Folder = folder;
    }

    [Fact]
    public async Task DirectExportWorksWithoutPlaybackAndWritesExactPcmWithRiffPadding()
    {
        var dir = Directory.CreateTempSubdirectory("opentalkit-test-");
        try
        {
            var player = new Player { UnavailableReason = "No audio device" };
            using var backend = new NativeTiSpeechBackend(player, new Synth());
            Assert.False(backend.Open());
            var file = Path.Combine(dir.FullName, "voice.wav");
            var service = new ExportService(file);
            using var vm = new TalkControlViewModel(backend, Parameters, service, engineAvailable: false) { Text = "hello" };
            Assert.False(vm.TalkCommand.CanExecute(null));
            Assert.True(vm.ExportCommand.CanExecute(null));
            await vm.ExportCommand.ExecuteAsync(null);
            Assert.Null(vm.StatusMessage);
            var wave = await File.ReadAllBytesAsync(file);
            Assert.Equal("RIFF", Encoding.ASCII.GetString(wave, 0, 4));
            Assert.Equal(40, BitConverter.ToInt32(wave, 4));
            Assert.Equal(11025, BitConverter.ToInt32(wave, 24));
            Assert.Equal(3, BitConverter.ToInt32(wave, 40));
            Assert.Equal(new byte[] { 128, 140, 120, 0 }, wave[44..]);
            Assert.Empty(player.Played);
            Assert.Equal(dir.FullName, service.Folder);
        }
        finally { dir.Delete(true); }
    }

    [Fact]
    public async Task FailedExportPreservesAnExistingFile()
    {
        var dir = Directory.CreateTempSubdirectory("opentalkit-test-");
        try
        {
            var path = Path.Combine(dir.FullName, "voice.wav");
            await File.WriteAllTextAsync(path, "keep me");
            var synth = new Synth { Generate = (_, _) => TiSynthesisResult.Failure(TiStatus.NoLanguage, "English only") };
            using var backend = new NativeTiSpeechBackend(new Player(), synth);
            using var vm = new TalkControlViewModel(backend, Parameters, new ExportService(path), false) { Text = "hello" };
            await vm.ExportCommand.ExecuteAsync(null);
            Assert.Equal("English only", vm.StatusMessage);
            Assert.Equal("keep me", await File.ReadAllTextAsync(path));
        }
        finally { dir.Delete(true); }
    }
}
