using System;
using System.Runtime.InteropServices.JavaScript;
using System.Threading.Tasks;

namespace OpenTalkIt.Browser;

/// <summary>wwwroot/opentalkit.js, registered by main.js under <see cref="Module"/>.</summary>
internal static partial class BrowserInterop
{
    private const string Module = "opentalkit";

    [JSImport("audioUnavailableReason", Module)]
    public static partial string? AudioUnavailableReason();

    /// <summary>Copies the engine's unsigned 8-bit mono samples into the next utterance.</summary>
    [JSImport("loadPcm", Module)]
    public static partial void LoadPcm([JSMarshalAs<JSType.MemoryView>] Span<byte> samples, int sampleRate);

    /// <summary>Starts the loaded utterance; completes when it ends or is stopped.</summary>
    [JSImport("play", Module)]
    public static partial Task Play();

    [JSImport("stop", Module)]
    public static partial void Stop();

    [JSImport("pause", Module)]
    public static partial void Pause();

    [JSImport("resume", Module)]
    public static partial void Resume();

    /// <summary>Hands the bytes to the browser as a file download.</summary>
    [JSImport("download", Module)]
    public static partial void Download(string fileName, [JSMarshalAs<JSType.MemoryView>] Span<byte> bytes, string mimeType);
}
