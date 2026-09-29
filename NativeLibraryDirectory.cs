using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.Loader;

namespace OpenTalkIt;

/// <summary>
/// Release builds for Windows and Linux are a single executable with every
/// native library (Skia, HarfBuzz, ANGLE, libtispeech) in a <c>native</c>
/// folder beside it. Nothing is extracted to a temp directory, so the runtime
/// has to be told where that folder is before Avalonia touches Skia.
/// A no-op when the folder does not exist (dev builds, the macOS bundle).
/// </summary>
internal static class NativeLibraryDirectory
{
    public static void Register()
    {
        var dir = Path.Combine(AppContext.BaseDirectory, "native");
        if (!Directory.Exists(dir))
            return;

        // ANGLE (av_libglesv2) is loaded by Avalonia.Win32 through LoadLibrary,
        // which only sees the folder via the process DLL search path.
        if (OperatingSystem.IsWindows())
            SetDllDirectoryW(dir);

        // Skia and HarfBuzz come in through DllImport. This event fires only
        // after the default probe (the executable's folder) has failed.
        AssemblyLoadContext.Default.ResolvingUnmanagedDll += (_, name) => Load(dir, name);
    }

    private static IntPtr Load(string dir, string name)
    {
        var ext = OperatingSystem.IsWindows() ? ".dll" : OperatingSystem.IsMacOS() ? ".dylib" : ".so";
        foreach (var file in new[] { name, name + ext, "lib" + name + ext })
        {
            if (NativeLibrary.TryLoad(Path.Combine(dir, file), out var handle))
                return handle;
        }
        return IntPtr.Zero;
    }

    [DllImport("kernel32", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetDllDirectoryW(string path);
}
