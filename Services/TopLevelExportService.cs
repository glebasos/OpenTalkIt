using System;
using System.IO;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Platform.Storage;

namespace OpenTalkIt.Services;

public sealed class TopLevelExportService : IExportService
{
    private readonly Func<TopLevel?> _topLevel;
    private readonly Settings _settings;

    // Set when the picker returned a document with no local path (Android's
    // content:// URIs): the WAV is written to this staging file first and
    // copied into the document by CompleteAsync.
    private (string Path, IStorageFile File)? _staged;

    /// <param name="topLevel">Resolved on every export: on Android the view,
    /// and so its TopLevel, is recreated with the activity.</param>
    public TopLevelExportService(Func<TopLevel?> topLevel)
    {
        _topLevel = topLevel;
        _settings = Settings.Load();
    }

    public async Task<string?> PickSaveFileAsync(string suggestedName)
    {
        var storage = _topLevel()?.StorageProvider;
        if (storage is null) return null;

        IStorageFolder? startFolder = null;
        if (!string.IsNullOrEmpty(_settings.LastExportFolder) && Directory.Exists(_settings.LastExportFolder))
        {
            startFolder = await storage.TryGetFolderFromPathAsync(_settings.LastExportFolder);
        }

        var file = await storage.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Export WAV",
            SuggestedFileName = suggestedName,
            DefaultExtension = "wav",
            FileTypeChoices = new[]
            {
                new FilePickerFileType("WAV audio") { Patterns = new[] { "*.wav" }, MimeTypes = new[] { "audio/wav" } }
            },
            SuggestedStartLocation = startFolder,
        });
        if (file is null) return null;

        if (file.TryGetLocalPath() is { } local)
        {
            file.Dispose();
            return local;
        }

        _staged?.File.Dispose();
        var staging = Path.Combine(Path.GetTempPath(), $"opentalkit-export-{Guid.NewGuid():N}.wav");
        _staged = (staging, file);
        return staging;
    }

    public async Task CompleteAsync(string path)
    {
        if (_staged is not { } staged || staged.Path != path)
        {
            var folder = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(folder)) RememberFolder(folder);
            return;
        }

        _staged = null;
        try
        {
            await using var source = File.OpenRead(path);
            await using var destination = await staged.File.OpenWriteAsync();
            await source.CopyToAsync(destination);
        }
        finally
        {
            staged.File.Dispose();
            File.Delete(path);
        }
    }

    public void RememberFolder(string path)
    {
        _settings.LastExportFolder = path;
        _settings.Save();
    }
}
