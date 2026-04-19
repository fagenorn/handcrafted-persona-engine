using System.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.ML.OnnxRuntime;
using Serilog;
using ILogger = Serilog.ILogger;

namespace PersonaEngine.App;

/// <summary>
///     Runs environment checks before the DI container is built.
///     Catches missing CUDA, espeak-ng, and config issues early with actionable messages
///     instead of cryptic native-loader exceptions deep in service resolution.
///     Model-existence probes (Whisper, Kokoro, Silero, OpenNLP, Live2D avatars) are
///     intentionally absent: <see cref="PersonaEngine.Lib.Assets.IAssetCatalog" /> is the
///     single source of truth for what is installed, and the bootstrapper that runs
///     before this validator already provisions every required asset before the host
///     reaches DI construction.
/// </summary>
internal static class StartupValidator
{
    /// <returns>true if no errors were found and startup can proceed.</returns>
    public static bool Run(IConfiguration config)
    {
        var log = Log.ForContext("SourceContext", "Startup");

        log.Information("Validating environment...");

        var errors = 0;
        var warnings = 0;

        CheckGpu(log, ref errors);
        CheckCuda(log, ref errors);
        CheckEspeakNg(log, config, ref errors);
        CheckPrompt(log, config, ref warnings);

        if (errors > 0)
        {
            log.Error(
                "Startup validation failed with {ErrorCount} error(s). Fix them before starting",
                errors
            );
        }
        else if (warnings > 0)
        {
            log.Information("Startup validation passed with {WarningCount} warning(s)", warnings);
        }
        else
        {
            log.Information("Startup validation passed");
        }

        return errors == 0;
    }

    private static void CheckGpu(ILogger log, ref int errors)
    {
        try
        {
            using var process = new Process();
            process.StartInfo = new ProcessStartInfo
            {
                FileName = "nvidia-smi",
                Arguments = "--query-gpu=name --format=csv,noheader",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            process.Start();
            var output = process.StandardOutput.ReadToEnd().Trim();

            if (!process.WaitForExit(5000))
            {
                process.Kill();
                log.Error("GPU: nvidia-smi timed out. Ensure NVIDIA drivers are installed");
                errors++;
                return;
            }

            if (process.ExitCode == 0 && !string.IsNullOrWhiteSpace(output))
            {
                var gpuName = output.Split('\n')[0].Trim();
                log.Information("GPU: {GpuName}", gpuName);
            }
            else
            {
                log.Error(
                    "GPU: No NVIDIA GPU detected. An NVIDIA GPU with CUDA support is required"
                );
                errors++;
            }
        }
        catch
        {
            log.Error(
                "GPU: nvidia-smi not found. Install NVIDIA drivers from https://www.nvidia.com/Download/index.aspx"
            );
            errors++;
        }
    }

    private static void CheckCuda(ILogger log, ref int errors)
    {
        try
        {
            using var opts = new SessionOptions();
            opts.LogSeverityLevel = OrtLoggingLevel.ORT_LOGGING_LEVEL_ERROR;
            opts.AppendExecutionProvider_CUDA();
            log.Information("CUDA: Execution provider available");
        }
        catch (Exception ex)
        {
            var detail =
                ex.Message.Contains("cudnn", StringComparison.OrdinalIgnoreCase)
                    ? "cuDNN libraries not found"
                : ex.Message.Contains("cuda", StringComparison.OrdinalIgnoreCase)
                    ? "CUDA runtime libraries not found"
                : $"CUDA provider failed: {Truncate(ex.Message, 80)}";

            log.Error(
                "CUDA: {Detail}. Ensure NVIDIA drivers are up to date and native/ folder contains CUDA/cuDNN DLLs. See INSTALLATION.md section 2",
                detail
            );
            errors++;
        }
    }

    private static void CheckEspeakNg(ILogger log, IConfiguration config, ref int errors)
    {
        var espeakPath = config["Config:Tts:EspeakPath"] ?? "espeak-ng";

        try
        {
            using var process = new Process();
            process.StartInfo = new ProcessStartInfo
            {
                FileName = espeakPath,
                Arguments = "--version",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            process.Start();

            // espeak-ng writes version info to stderr on some platforms
            var stdout = process.StandardOutput.ReadToEnd().Trim();
            var stderr = process.StandardError.ReadToEnd().Trim();

            if (!process.WaitForExit(5000))
            {
                process.Kill();
            }

            var version = !string.IsNullOrEmpty(stdout) ? stdout : stderr;

            if (!string.IsNullOrWhiteSpace(version))
            {
                log.Information("espeak-ng: {Version}", Truncate(version, 50));
            }
            else
            {
                log.Error(
                    "espeak-ng: '{EspeakPath}' produced no output. Reinstall and ensure it is on PATH, or set Config:Tts:EspeakPath",
                    espeakPath
                );
                errors++;
            }
        }
        catch
        {
            log.Error(
                "espeak-ng: '{EspeakPath}' not found. Install espeak-ng and add to PATH, or set Config:Tts:EspeakPath in appsettings.json",
                espeakPath
            );
            errors++;
        }
    }

    private static void CheckPrompt(ILogger log, IConfiguration config, ref int warnings)
    {
        var promptFile = config["Config:ConversationContext:SystemPromptFile"] ?? "personality.txt";
        var fullPath = Path.Combine(AppContext.BaseDirectory, "Resources", "Prompts", promptFile);

        if (File.Exists(fullPath))
        {
            log.Information("Prompt: {PromptFile}", promptFile);
        }
        else
        {
            log.Warning(
                "Prompt: {PromptFile} not found in Resources/Prompts/. Create a personality prompt file. See INSTALLATION.md section 4",
                promptFile
            );
            warnings++;
        }
    }

    private static string Truncate(string value, int maxLength) =>
        value.Length <= maxLength ? value : value[..(maxLength - 3)] + "...";
}
