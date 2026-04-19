using System.Runtime.InteropServices;
using System.Text;
using LLama.Native;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.ML.OnnxRuntime;
using PersonaEngine.Lib;
using PersonaEngine.Lib.Bootstrapper;
using PersonaEngine.Lib.Core;
using Serilog;
using Serilog.Events;

namespace PersonaEngine.App;

internal static class Program
{
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool SetDllDirectory(string lpPathName);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr LoadLibraryEx(string lpLibFileName, IntPtr hFile, uint dwFlags);

    private const uint LoadWithAlteredSearchPath = 0x00000008;

    private static async Task<int> Main(string[] args)
    {
        var nativeDir = Path.Combine(AppContext.BaseDirectory, "native");
        if (Directory.Exists(nativeDir))
        {
            SetDllDirectory(nativeDir);
        }

        Console.OutputEncoding = Encoding.UTF8;

        // LLamaSharp CUDA12 backend: ggml.dll imports ggml-cpu.dll which isn't in the
        // cuda12/ dir. Pre-load it from the avx2 dir, then load the CUDA12 chain.
        var llamaNativeDir = Path.Combine(
            AppContext.BaseDirectory,
            "runtimes",
            "win-x64",
            "native"
        );
        var llamaCudaDir = Path.Combine(llamaNativeDir, "cuda12");
        var llamaDll = Path.Combine(llamaCudaDir, "llama.dll");
        if (File.Exists(llamaDll))
        {
            // ggml-cpu.dll is in the avx2/ (or avx/) sibling directory
            var cpuDll = Path.Combine(llamaNativeDir, "avx2", "ggml-cpu.dll");
            if (!File.Exists(cpuDll))
            {
                cpuDll = Path.Combine(llamaNativeDir, "avx", "ggml-cpu.dll");
            }

            if (File.Exists(cpuDll))
            {
                LoadLibraryEx(cpuDll, IntPtr.Zero, LoadWithAlteredSearchPath);
            }

            if (LoadLibraryEx(llamaDll, IntPtr.Zero, LoadWithAlteredSearchPath) == IntPtr.Zero)
            {
                throw new DllNotFoundException(
                    $"Failed to load {llamaDll}. Check that all native dependencies are present."
                );
            }

            NativeLibraryConfig.LLama.WithLibrary(llamaDll);
        }

        CreateLogger();

        var llamaLogger = Log.ForContext("SourceContext", "llama.cpp");
        NativeLibraryConfig.LLama.WithLogCallback(
            (level, message) =>
            {
                var msg = message.TrimEnd('\n', '\r');
                if (string.IsNullOrEmpty(msg))
                    return;

                switch (level)
                {
                    case LLamaLogLevel.Error:
                        llamaLogger.Error("{Message}", msg);
                        // ggml/llama call abort() shortly after emitting an error. Flush
                        // the managed Console buffer synchronously so the line survives.
                        try
                        {
                            Console.Out.Flush();
                            Console.Error.Flush();
                        }
                        catch
                        {
                            // Ignore — we're on a best-effort pre-abort path
                        }

                        break;
                    case LLamaLogLevel.Warning:
                        llamaLogger.Warning("{Message}", msg);
                        break;
                    case LLamaLogLevel.Info:
                        llamaLogger.Information("{Message}", msg);
                        break;
                    case LLamaLogLevel.Debug:
                        llamaLogger.Debug("{Message}", msg);
                        break;
                }
            }
        );

        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            if (e.ExceptionObject is Exception ex)
            {
                Log.Fatal(ex, "Unhandled exception (terminating={Terminating})", e.IsTerminating);
            }
            else
            {
                Log.Fatal("Unhandled non-Exception object: {Object}", e.ExceptionObject);
            }

            Log.CloseAndFlush();
        };

        AppDomain.CurrentDomain.ProcessExit += (_, _) =>
        {
            Log.CloseAndFlush();
        };

        // Suppress ONNX Runtime warnings globally before any sessions are created
        OrtEnv.Instance().EnvLogLevel = OrtLoggingLevel.ORT_LOGGING_LEVEL_ERROR;

        // ── Bootstrap ────────────────────────────────────────────────────────────
        // Run the asset bootstrapper before any subsystem that depends on models
        // or native runtimes. On failure we exit with a non-zero code so launchers
        // can surface the error; on success we continue into the main DI graph.
        // Bootstrap runs before IConfiguration is built, so it sees only CLI args
        // and the embedded manifest — any future bootstrap setting must be a CLI
        // flag, not an appsettings.json entry.
        var parsedArgs = CommandLineArgs.Parse(args);

        var bootstrapServices = new ServiceCollection();
        bootstrapServices.AddBootstrapper(parsedArgs.NonInteractive);

        await using (var bootstrapProvider = bootstrapServices.BuildServiceProvider())
        {
            var runner = bootstrapProvider.GetRequiredService<BootstrapRunner>();
            var bootstrapResult = await runner.RunAsync(
                parsedArgs.Bootstrap,
                CancellationToken.None
            );

            if (!bootstrapResult.Success)
            {
                Log.Fatal("Bootstrap failed: {Reason}", bootstrapResult.ErrorMessage);
                await Log.CloseAndFlushAsync();
                Console.Error.WriteLine($"Bootstrap failed: {bootstrapResult.ErrorMessage}");
                return 1;
            }
        }

        // ── CUDA / cuDNN pre-load ────────────────────────────────────────────────
        // The bootstrapper drops NVIDIA redistributables under Resources/cuda/<pkg>
        // and Resources/cudnn (see Assets/Manifest/install-manifest.json). The
        // single SetDllDirectory call above only covers <BaseDir>/native, so we
        // pre-load every bootstrapped CUDA DLL here via LoadLibraryEx with
        // LoadWithAlteredSearchPath. Once mapped, ONNX Runtime GPU and
        // Whisper.net's CUDA backend resolve their imports against these copies
        // regardless of the process DLL search path.
        //
        // Order matters: cudart must come first so cublas/cublasLt/cufft can
        // resolve their cudart imports during their own load. cudnn last as it
        // depends on cublas/cublasLt.
        PreloadCudaRuntime();

        var builder = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json", false, true);

        IConfiguration config = builder.Build();

        if (!StartupValidator.Run(config))
        {
            await Log.CloseAndFlushAsync();
            Console.WriteLine();
            Console.WriteLine("Press any key to exit...");
            Console.ReadKey(true);
            return 1;
        }

        var services = new ServiceCollection();

        services.AddLogging(loggingBuilder =>
        {
            loggingBuilder.AddSerilog();
        });

        services.AddMetrics();
        services.AddApp(config);

        var serviceProvider = services.BuildServiceProvider();

        var window = serviceProvider.GetRequiredService<AvatarApp>();
        window.Run();

        await serviceProvider.DisposeAsync();
        return 0;
    }

    private static void PreloadCudaRuntime()
    {
        // Ordered to satisfy CUDA's load-time dependency chain. Each entry is a
        // path under <BaseDir>/Resources mirroring the manifest's installPath.
        var loadOrder = new[] { "cuda/cudart", "cuda/cublas", "cuda/cufft", "cudnn" };

        foreach (var relative in loadOrder)
        {
            var dir = Path.Combine(
                AppContext.BaseDirectory,
                "Resources",
                relative.Replace('/', Path.DirectorySeparatorChar)
            );
            if (!Directory.Exists(dir))
            {
                Log.Debug("CUDA pre-load: skipping missing directory {Dir}", dir);
                continue;
            }

            foreach (var dll in Directory.EnumerateFiles(dir, "*.dll", SearchOption.AllDirectories))
            {
                if (LoadLibraryEx(dll, IntPtr.Zero, LoadWithAlteredSearchPath) == IntPtr.Zero)
                {
                    var err = Marshal.GetLastWin32Error();
                    Log.Warning(
                        "CUDA pre-load: LoadLibraryEx failed for {Dll} (Win32 error {Err})",
                        dll,
                        err
                    );
                }
            }
        }
    }

    private static void CreateLogger()
    {
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Warning()
            .MinimumLevel.Override("PersonaEngine.Lib.Core.Conversation", LogEventLevel.Information)
            .MinimumLevel.Override(
                "PersonaEngine.Lib.TTS.Synthesis.Alignment",
                LogEventLevel.Information
            )
            .MinimumLevel.Override(
                "PersonaEngine.Lib.TTS.Synthesis.LipSync",
                LogEventLevel.Information
            )
            .MinimumLevel.Override(
                "PersonaEngine.Lib.TTS.Synthesis.Engine.SentenceProcessor",
                LogEventLevel.Information
            )
            .MinimumLevel.Override("Startup", LogEventLevel.Information)
            .MinimumLevel.Override("PersonaEngine.Lib.UI.Overlay", LogEventLevel.Information)
            .MinimumLevel.Override("llama.cpp", LogEventLevel.Error)
            .Enrich.FromLogContext()
            .Enrich.With<GuidToEmojiEnricher>()
            .WriteTo.Console(
                outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}"
            )
            .CreateLogger();
    }
}
