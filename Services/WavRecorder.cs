using System;
using System.Threading.Tasks;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace OpenTalkIt.Services;

/// <summary>
/// Captures the system audio mix via WASAPI loopback and writes it to a WAV file.
/// TiSpeech plays through the default output device and exposes no file-export API,
/// so loopback capture is the only way to grab the generated voice as a file.
///
/// When <see cref="Start"/> is called with <c>silentPlayback</c>, the default render
/// endpoint is muted for the duration of the capture. WASAPI loopback taps the render
/// stream before the endpoint mute stage, so the WAV still receives full audio while
/// the user hears nothing.
/// </summary>
public sealed class WavRecorder : IDisposable
{
    private WasapiLoopbackCapture? _capture;
    private WaveFileWriter? _writer;
    private TaskCompletionSource? _stopped;

    private MMDevice? _device;
    private bool? _previousMute;

    /// <summary>
    /// NAudio's WASAPI loopback capture (and the endpoint-mute APIs used for
    /// silent playback) are Windows-only — there is no macOS/Linux
    /// implementation in NAudio. Callers should check this before
    /// constructing/using <see cref="WavRecorder"/>; see
    /// <c>TalkControlViewModel.IsExportSupported</c>, which already gates the
    /// UI's Export command on it. This throws rather than silently no-op'ing
    /// so a future caller that forgets the check fails loudly instead of
    /// producing an empty/missing WAV file.
    /// </summary>
    public static bool IsSupported => OperatingSystem.IsWindows();

    public void Start(string filePath, bool silentPlayback = false)
    {
        if (!IsSupported)
            throw new PlatformNotSupportedException(
                "WavRecorder uses WASAPI loopback capture (NAudio), which is only available on Windows.");

        using (var enumerator = new MMDeviceEnumerator())
        {
            _device = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
        }

        if (silentPlayback)
        {
            try
            {
                _previousMute = _device.AudioEndpointVolume.Mute;
                _device.AudioEndpointVolume.Mute = true;
            }
            catch
            {
                _previousMute = null;
            }
        }

        _capture = new WasapiLoopbackCapture(_device);
        _writer = new WaveFileWriter(filePath, _capture.WaveFormat);
        _stopped = new TaskCompletionSource();

        _capture.DataAvailable += (_, e) =>
        {
            _writer?.Write(e.Buffer, 0, e.BytesRecorded);
        };
        _capture.RecordingStopped += (_, _) =>
        {
            _writer?.Dispose();
            _writer = null;
            _capture?.Dispose();
            _capture = null;
            RestoreMute();
            _stopped?.TrySetResult();
        };

        _capture.StartRecording();
    }

    public Task StopAsync()
    {
        if (_capture is null || _stopped is null) return Task.CompletedTask;
        _capture.StopRecording();
        return _stopped.Task;
    }

    private void RestoreMute()
    {
        if (_previousMute is bool prev && _device is not null)
        {
            try { _device.AudioEndpointVolume.Mute = prev; }
            catch { }
            _previousMute = null;
        }
    }

    public void Dispose()
    {
        _capture?.Dispose();
        _writer?.Dispose();
        RestoreMute();
        _device?.Dispose();
        _device = null;
    }
}
