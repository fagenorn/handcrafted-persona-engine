using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using PersonaEngine.Lib;
using PersonaEngine.Lib.Bootstrapper;
using PersonaEngine.Lib.Core;
using Serilog;

namespace PersonaEngine.App;

internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;

        LoggingConfiguration.ConfigureSerilog();
        LoggingConfiguration.InstallGlobalExceptionHandlers();

        // Register <BaseDir>/native as an extra DLL search directory for loose
        // native libs relocated by the publish target. This only needs to happen
        // once and has no dependency on CUDA assets.
        NativeLibraryLoader.RegisterNativeSearchDirectory();

        // ── Bootstrap ────────────────────────────────────────────────────────────
        // Run the asset bootstrapper before any subsystem that depends on models
        // or native runtimes. On failure we exit with a non-zero code so launchers
        // can surface the error; on success we continue into the main DI graph.
        // Bootstrap runs before IConfiguration is built, so it sees only CLI args
        // and the embedded manifest — any future bootstrap setting must be a CLI
        // flag, not an appsettings.json entry.
        var parsedArgs = CommandLineArgs.Parse(args);
        if (!await RunBootstrapAsync(parsedArgs).ConfigureAwait(false))
        {
            await Log.CloseAndFlushAsync();
            return 1;
        }

        // ── Native CUDA / LLama pre-load (order matters) ─────────────────────────
        // 1. PreloadCudaRuntime registers the bootstrapped cudart/cublas/cufft/cudnn
        //    DLLs with the Windows loader. Once mapped, ONNX Runtime GPU and
        //    Whisper.net resolve their imports against these copies regardless of
        //    the process DLL search path.
        // 2. PreloadLlamaBackend then loads cuda12/llama.dll, whose transitive
        //    dependency on ggml-cuda.dll -> cudart64_12.dll/cublas64_12.dll can
        //    only resolve once step 1 has completed. Calling step 2 before step 1
        //    on a system without a global CUDA install is what produced the
        //    `llama.dll: DllNotFoundException` users hit on fresh installs.
        // 3. BridgeLlamaLogging wires LLamaSharp's log callback after the backend
        //    is known-good, so load-time diagnostics flow through Serilog.
        NativeLibraryLoader.PreloadCudaRuntime();
        NativeLibraryLoader.PreloadLlamaBackend();
        LoggingConfiguration.BridgeLlamaLogging();

        // OrtEnv.Instance() triggers the GPU-enabled onnxruntime.dll load, which itself
        // imports cudart64_12.dll/cublas64_12.dll. Must run after PreloadCudaRuntime
        // so those imports resolve against the bootstrapped copies.
        LoggingConfiguration.SuppressOnnxRuntimeWarnings();

        // ── Configuration + validation ───────────────────────────────────────────
        IConfiguration config = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
            .Build();

        if (!StartupValidator.Run(config))
        {
            await Log.CloseAndFlushAsync();
            Console.WriteLine();
            Console.WriteLine("Press any key to exit...");
            Console.ReadKey(intercept: true);
            return 1;
        }

        // ── Application composition ──────────────────────────────────────────────
        var services = new ServiceCollection();
        services.AddLogging(b => b.AddSerilog());
        services.AddMetrics();
        services.AddApp(config);

        await using var serviceProvider = services.BuildServiceProvider();
        var window = serviceProvider.GetRequiredService<AvatarApp>();
        window.Run();

        return 0;
    }

    /// <summary>
    ///     Runs the asset bootstrapper in an isolated DI scope. Returns <c>true</c> when
    ///     the bootstrap pipeline completes successfully or there's nothing to do.
    /// </summary>
    private static async Task<bool> RunBootstrapAsync(CommandLineArgs parsedArgs)
    {
        var bootstrapServices = new ServiceCollection();
        bootstrapServices.AddBootstrapper(parsedArgs.NonInteractive);
        // Wire MEL through the static Serilog logger so bootstrap-time diagnostics
        // (per-asset download failures, HF/NVIDIA retries, plan details) reach the
        // console sink. Without this, ILogger<T> in the bootstrap graph no-ops and
        // the only operator-visible signal is the final "Bootstrap failed" FTL line.
        bootstrapServices.AddLogging(b => b.AddSerilog(dispose: false));

        await using var bootstrapProvider = bootstrapServices.BuildServiceProvider();
        var runner = bootstrapProvider.GetRequiredService<BootstrapRunner>();
        var result = await runner
            .RunAsync(parsedArgs.Bootstrap, CancellationToken.None)
            .ConfigureAwait(false);

        if (result.Success)
        {
            return true;
        }

        Log.Fatal("Bootstrap failed: {Reason}", result.ErrorMessage);
        Console.Error.WriteLine($"Bootstrap failed: {result.ErrorMessage}");
        return false;
    }
}
