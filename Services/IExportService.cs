using System.Threading.Tasks;

namespace OpenTalkIt.Services;

public interface IExportService
{
    Task<string?> PickSaveFileAsync(string suggestedName);
    void RememberFolder(string path);
}
