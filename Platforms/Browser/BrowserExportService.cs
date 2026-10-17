using System.IO;
using System.Threading.Tasks;
using OpenTalkIt.Services;

namespace OpenTalkIt.Browser;

/// <summary>
/// The WAV is written to the in-memory file system, then handed to the browser
/// as a download. No save picker: showSaveFilePicker is blocked inside the
/// cross-origin iframe itch.io runs the page in, and missing outside Chromium.
/// </summary>
public sealed class BrowserExportService : IExportService
{
    public Task<string?> PickSaveFileAsync(string suggestedName)
    {
        var directory = Path.Combine(Path.GetTempPath(), "opentalkit-export");
        Directory.CreateDirectory(directory);
        return Task.FromResult<string?>(Path.Combine(directory, suggestedName));
    }

    public void RememberFolder(string path) { }

    public Task CompleteAsync(string path)
    {
        try { BrowserInterop.Download(Path.GetFileName(path), File.ReadAllBytes(path), "audio/wav"); }
        finally { File.Delete(path); }
        return Task.CompletedTask;
    }
}
