using System.IO;
using System.Threading.Tasks;

namespace OpenTalkIt.Services;

public interface IExportService
{
    /// <summary>A local path the WAV should be written to, or null when cancelled.</summary>
    Task<string?> PickSaveFileAsync(string suggestedName);
    void RememberFolder(string path);

    /// <summary>
    /// Called once the WAV at <paramref name="path"/> is complete. A picker that
    /// hands out a document URI instead of a path (Android) copies it there.
    /// </summary>
    Task CompleteAsync(string path)
    {
        var folder = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(folder)) RememberFolder(folder);
        return Task.CompletedTask;
    }
}
