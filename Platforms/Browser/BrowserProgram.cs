using System.Runtime.Versioning;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Browser;

[assembly: SupportedOSPlatform("browser")]

namespace OpenTalkIt.Browser;

/// <summary>WebAssembly entry point; wwwroot/main.js starts it inside #out.</summary>
internal static class BrowserProgram
{
    private static Task Main(string[] args) =>
        AppBuilder.Configure<App>()
            .WithInterFont()
            .LogToTrace()
            .StartBrowserAppAsync("out");
}
