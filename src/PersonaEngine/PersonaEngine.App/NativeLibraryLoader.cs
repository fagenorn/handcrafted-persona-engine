using System.Runtime.InteropServices;
using LLama.Native;
using Serilog;
using ILogger = Serilog.ILogger;

namespace PersonaEngine.App;

/// <summary>
///     Orchestrates native DLL loading for the app's CUDA-backed runtimes.
///     <para>
///         Windows only resolves import tables through <c>LoadLibrary</c>'s search path or
///         whatever is already loaded in the process. The bootstrapper drops CUDA/cuDNN
///         redistributables under <c>Resources/&lt;pkg&gt;</c>, which is <em>not</em> on any
///         default search path. We therefore pre-load every bootstrapped DLL via
///         <c>LoadLibraryExW</c> with <c>LOAD_WITH_ALTERED_SEARCH_PATH</c> before any
///         managed code (ONNX Runtime GPU, Whisper.net, LLamaSharp) triggers a transitive
///         native load that would otherwise fail.
///     </para>
///     <para>
///         Load order matters: <c>cudart</c> must come before libraries that import it
///         (<c>cublas</c>, <c>cublasLt</c>, <c>cufft</c>); <c>cudnn</c> comes last because
///         it depends on <c>cublas/cublasLt</c>. CUDA 12 (ONNX Runtime GPU) and CUDA 13
///         (Whisper.net's <c>ggml-cuda-whisper.dll</c>) coexist under separate suffixed dirs.
///     </para>
/// </summary>
internal static partial class NativeLibraryLoader
{
    private const uint LoadWithAlteredSearchPath = 0x00000008;

    /// <summary>
    ///     Paths (relative to <c>&lt;BaseDir&gt;/Resources</c>) mirroring the install
    ///     manifest's <c>installPath</c>. Ordered to satisfy CUDA's load-time dependency chain.
    /// </summary>
    private static readonly string[] CudaPreloadOrder =
    [
        "cuda/cudart",
        "cuda/cudart-v13",
        "cuda/cublas",
        "cuda/cublas-v13",
        "cuda/cufft",
        "cudnn",
    ];

    [LibraryImport(
        "kernel32.dll",
        EntryPoint = "SetDllDirectoryW",
        SetLastError = true,
        StringMarshalling = StringMarshalling.Utf16
    )]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetDllDirectory(string? lpPathName);

    [LibraryImport(
        "kernel32.dll",
        EntryPoint = "LoadLibraryExW",
        SetLastError = true,
        StringMarshalling = StringMarshalling.Utf16
    )]
    private static partial IntPtr LoadLibraryEx(string lpLibFileName, IntPtr hFile, uint dwFlags);

    /// <summary>
    ///     Registers <c>&lt;BaseDir&gt;/native</c> as an additional DLL search directory.
    ///     The <c>native</c> folder holds loose DLLs relocated by the publish target.
    /// </summary>
    public static void RegisterNativeSearchDirectory()
    {
        var nativeDir = Path.Combine(AppContext.BaseDirectory, "native");
        if (Directory.Exists(nativeDir))
        {
            SetDllDirectory(nativeDir);
        }
    }

    /// <summary>
    ///     Pre-loads every bootstrapped CUDA/cuDNN DLL into the process. After this call,
    ///     subsequent imports against the same DLL names resolve to these copies regardless
    ///     of the process DLL search path.
    /// </summary>
    public static void PreloadCudaRuntime(ILogger? logger = null)
    {
        logger ??= Log.Logger;

        foreach (var relative in CudaPreloadOrder)
        {
            var dir = Path.Combine(
                AppContext.BaseDirectory,
                "Resources",
                relative.Replace('/', Path.DirectorySeparatorChar)
            );

            if (!Directory.Exists(dir))
            {
                logger.Debug("CUDA pre-load: skipping missing directory {Dir}", dir);
                continue;
            }

            foreach (var dll in Directory.EnumerateFiles(dir, "*.dll", SearchOption.AllDirectories))
            {
                TryPreloadNative(dll, logger);
            }
        }
    }

    /// <summary>
    ///     Pre-loads LLamaSharp's CUDA12 <c>llama.dll</c> and its split <c>ggml-cpu.dll</c>
    ///     sibling, then pins the library path in <see cref="NativeLibraryConfig" />.
    ///     <para>
    ///         Must run <em>after</em> <see cref="PreloadCudaRuntime" />, otherwise
    ///         <c>ggml-cuda.dll</c>'s transitive imports (<c>cudart64_12.dll</c>,
    ///         <c>cublas64_12.dll</c>) fail to resolve on systems without a global CUDA
    ///         install on <c>PATH</c>.
    ///     </para>
    /// </summary>
    public static void PreloadLlamaBackend(ILogger? logger = null)
    {
        logger ??= Log.Logger;

        var llamaNativeDir = Path.Combine(
            AppContext.BaseDirectory,
            "runtimes",
            "win-x64",
            "native"
        );
        var llamaDll = Path.Combine(llamaNativeDir, "cuda12", "llama.dll");

        if (!File.Exists(llamaDll))
        {
            logger.Debug("LLama pre-load: {Dll} not found, skipping", llamaDll);
            return;
        }

        // ggml.dll imports ggml-cpu.dll which is not shipped under cuda12/.
        // Pre-load it from the avx2/ (or avx/ fallback) sibling directory so the
        // CUDA chain can satisfy its CPU-fallback import.
        var cpuDll = Path.Combine(llamaNativeDir, "avx2", "ggml-cpu.dll");
        if (!File.Exists(cpuDll))
        {
            cpuDll = Path.Combine(llamaNativeDir, "avx", "ggml-cpu.dll");
        }

        if (File.Exists(cpuDll))
        {
            TryPreloadNative(cpuDll, logger);
        }

        if (LoadLibraryEx(llamaDll, IntPtr.Zero, LoadWithAlteredSearchPath) == IntPtr.Zero)
        {
            var err = Marshal.GetLastWin32Error();
            throw new DllNotFoundException(
                $"Failed to load {llamaDll} (Win32 error {err}). "
                    + "Check that the CUDA redistributables under Resources/cuda have been bootstrapped "
                    + "and that PreloadCudaRuntime() ran before this call."
            );
        }

        NativeLibraryConfig.LLama.WithLibrary(llamaDll);
    }

    private static void TryPreloadNative(string dllPath, ILogger logger)
    {
        if (LoadLibraryEx(dllPath, IntPtr.Zero, LoadWithAlteredSearchPath) != IntPtr.Zero)
        {
            return;
        }

        var err = Marshal.GetLastWin32Error();
        logger.Warning(
            "Native pre-load: LoadLibraryEx failed for {Dll} (Win32 error {Err})",
            dllPath,
            err
        );
    }
}
