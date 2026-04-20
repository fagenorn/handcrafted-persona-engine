using System.Runtime.InteropServices;

namespace PersonaEngine.Lib.Bootstrapper.GpuPreflight;

/// <summary>
/// Fallback driver-presence check used only when <c>nvidia-smi</c> isn't
/// available: we try <c>LoadLibrary("nvcuda.dll")</c>. Any success means an
/// NVIDIA user-mode driver is mapped into the process, which is enough to
/// say "there is a driver here" even though it can't tell us the version.
/// </summary>
public sealed class NvcudaProbe : INvcudaProbe
{
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr LoadLibraryW(string lpLibFileName);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool FreeLibrary(IntPtr hModule);

    public bool TryLoadNvcuda()
    {
        if (!OperatingSystem.IsWindows())
        {
            return false;
        }

        var handle = LoadLibraryW("nvcuda.dll");
        if (handle == IntPtr.Zero)
        {
            return false;
        }

        // Release the ref — the point was to probe, not hold the driver pinned
        // into the installer process.
        FreeLibrary(handle);
        return true;
    }
}
