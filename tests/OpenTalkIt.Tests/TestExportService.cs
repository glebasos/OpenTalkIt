using System.Threading.Tasks;
using OpenTalkIt.Services;

namespace OpenTalkIt.Tests;

/// <summary>
/// Minimal <see cref="IExportService"/> stand-in so tests can exercise
/// ExportCommand's CanExecute wiring without an Avalonia Window/StorageProvider.
/// Never actually invoked in these tests (CanExecute checks stop it first when
/// the engine or WAV export isn't available), but must be non-null to match
/// the "export service present" branch of CanExport().
/// </summary>
internal sealed class TestExportService : IExportService
{
    public Task<string?> PickSaveFileAsync(string suggestedName) => Task.FromResult<string?>(null);

    public void RememberFolder(string path) { }
}
