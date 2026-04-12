using System.IO;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Platform.Storage;

namespace OpenTalkIt.Services;

public sealed class MainWindowExportService : IExportService
{
    private readonly Window _window;
    private readonly Settings _settings;

    public MainWindowExportService(Window window)
    {
        _window = window;
        _settings = Settings.Load();
    }

    public async Task<string?> PickSaveFileAsync(string suggestedName)
    {
        IStorageFolder? startFolder = null;
        if (!string.IsNullOrEmpty(_settings.LastExportFolder) && Directory.Exists(_settings.LastExportFolder))
        {
            startFolder = await _window.StorageProvider.TryGetFolderFromPathAsync(_settings.LastExportFolder);
        }

        var file = await _window.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Export WAV",
            SuggestedFileName = suggestedName,
            DefaultExtension = "wav",
            FileTypeChoices = new[]
            {
                new FilePickerFileType("WAV audio") { Patterns = new[] { "*.wav" } }
            },
            SuggestedStartLocation = startFolder,
        });

        return file?.TryGetLocalPath();
    }

    public void RememberFolder(string path)
    {
        _settings.LastExportFolder = path;
        _settings.Save();
    }
}
