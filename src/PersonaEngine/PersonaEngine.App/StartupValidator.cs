using System.Diagnostics;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.ML.OnnxRuntime;
using Serilog;
using ILogger = Serilog.ILogger;

namespace PersonaEngine.App;

/// <summary>
///     Runs environment checks before the DI container is built.
///     Catches missing CUDA, models, and config issues early with actionable messages
///     instead of cryptic native-loader exceptions deep in service resolution.
/// </summary>
internal static class StartupValidator
{
    private static readonly string ModelsDir = Path.Combine(
        Directory.GetCurrentDirectory(),
        "Resources",
        "Models"
    );

    /// <returns>true if no errors were found and startup can proceed.</returns>
    public static bool Run(IConfiguration config)
    {
        var log = Log.ForContext("SourceContext", "Startup");

        log.Information("Validating environment...");

        var errors = 0;
        var warnings = 0;

        CheckGpu(log, ref errors);
        CheckCuda(log, ref errors);
        CheckModels(log, ref errors);
        CheckEspeakNg(log, config, ref errors);
        CheckLlmConfig(log, config, ref errors);
        CheckLive2D(log, config, ref warnings);
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

    private static void CheckModels(ILogger log, ref int errors)
    {
        var required = new (string RelativePath, string Name)[]
        {
            ("ggml-large-v3-turbo.bin", "Whisper Turbo v3"),
            ("ggml-tiny.en.bin", "Whisper Tiny"),
            ("silero_vad_v5.onnx", "Silero VAD"),
            ("kokoro/model_slim.onnx", "Kokoro TTS"),
            ("kokoro/phoneme_to_id.txt", "Kokoro phoneme map"),
            ("opennlp", "OpenNLP"),
        };

        var missing = new List<string>();

        foreach (var (relativePath, name) in required)
        {
            var fullPath = Path.Combine(ModelsDir, relativePath);
            if (!Path.Exists(fullPath))
            {
                missing.Add(name);
            }
        }

        // Kokoro voices directory must exist and contain at least one voice file
        var voicesDir = Path.Combine(ModelsDir, "kokoro", "voices");
        if (!Directory.Exists(voicesDir) || !Directory.EnumerateFiles(voicesDir).Any())
        {
            missing.Add("Kokoro voices");
        }

        if (missing.Count == 0)
        {
            log.Information("Models: All {Count} required models found", required.Length + 1);
        }
        else
        {
            log.Error(
                "Models: Missing {Missing}. Download and place in Resources/Models/. See INSTALLATION.md section 4",
                string.Join(", ", missing)
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

    private static void CheckLlmConfig(ILogger log, IConfiguration config, ref int errors)
    {
        var textEndpoint = config["Config:Llm:TextEndpoint"];
        var textModel = config["Config:Llm:TextModel"];
        var textApiKey = config["Config:Llm:TextApiKey"];
        var visionEndpoint = config["Config:Llm:VisionEndpoint"];
        var visionModel = config["Config:Llm:VisionModel"];
        var visionApiKey = config["Config:Llm:VisionApiKey"];

        var formatIssues = new List<string>();

        ValidateEndpointFormat(textEndpoint, "TextEndpoint", formatIssues);

        if (string.IsNullOrWhiteSpace(textModel))
        {
            formatIssues.Add("TextModel not set");
        }

        if (!string.IsNullOrWhiteSpace(visionEndpoint))
        {
            ValidateEndpointFormat(visionEndpoint, "VisionEndpoint", formatIssues);
        }

        if (formatIssues.Count > 0)
        {
            log.Error(
                "LLM: {Issues}. Set Config:Llm:TextEndpoint and TextModel in appsettings.json",
                string.Join("; ", formatIssues)
            );
            errors++;
            return;
        }

        // Validate text endpoint is a working OpenAI-compatible API with the configured model
        ValidateOpenAiEndpoint(log, textEndpoint!, textModel!, textApiKey, "Text", ref errors);

        // Validate vision endpoint if configured
        if (!string.IsNullOrWhiteSpace(visionEndpoint) && !string.IsNullOrWhiteSpace(visionModel))
        {
            ValidateOpenAiEndpoint(
                log,
                visionEndpoint,
                visionModel,
                visionApiKey,
                "Vision",
                ref errors
            );
        }
    }

    private static void ValidateEndpointFormat(string? endpoint, string name, List<string> issues)
    {
        if (string.IsNullOrWhiteSpace(endpoint))
        {
            issues.Add($"{name} not set");
            return;
        }

        if (
            !Uri.TryCreate(endpoint, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
        )
        {
            issues.Add($"{name} '{Truncate(endpoint, 40)}' is not a valid HTTP/HTTPS URL");
        }
    }

    private static void ValidateOpenAiEndpoint(
        ILogger log,
        string endpoint,
        string model,
        string? apiKey,
        string label,
        ref int errors
    )
    {
        List<string>? availableModels;

        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };

            if (!string.IsNullOrWhiteSpace(apiKey) && apiKey != "sk-")
            {
                client.DefaultRequestHeaders.Authorization =
                    new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", apiKey);
            }

            var modelsUrl = endpoint.TrimEnd('/') + "/models";
            using var response = client.GetAsync(modelsUrl).GetAwaiter().GetResult();

            if (!response.IsSuccessStatusCode)
            {
                log.Error(
                    "LLM {Label}: {Endpoint}/models returned HTTP {StatusCode}. Is this an OpenAI-compatible API?",
                    label,
                    Truncate(endpoint, 40),
                    (int)response.StatusCode
                );
                errors++;
                return;
            }

            var json = response.Content.ReadFromJsonAsync<JsonElement>().GetAwaiter().GetResult();

            if (!json.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array)
            {
                log.Error(
                    "LLM {Label}: {Endpoint}/models did not return an OpenAI-compatible response (missing 'data' array)",
                    label,
                    Truncate(endpoint, 40)
                );
                errors++;
                return;
            }

            availableModels = new List<string>();
            foreach (var item in data.EnumerateArray())
            {
                if (item.TryGetProperty("id", out var id))
                {
                    availableModels.Add(id.GetString() ?? "");
                }
            }
        }
        catch (TaskCanceledException)
        {
            log.Error(
                "LLM {Label}: {Endpoint} timed out after 5s. Is the server running?",
                label,
                Truncate(endpoint, 40)
            );
            errors++;
            return;
        }
        catch (HttpRequestException ex)
        {
            log.Error(
                "LLM {Label}: {Endpoint} is not reachable: {Reason}",
                label,
                Truncate(endpoint, 40),
                Truncate(ex.InnerException?.Message ?? ex.Message, 80)
            );
            errors++;
            return;
        }

        if (
            availableModels.Count > 0
            && !availableModels.Any(m => m.Equals(model, StringComparison.OrdinalIgnoreCase))
        )
        {
            log.Error(
                "LLM {Label}: Model '{Model}' not found at {Endpoint}. Available: {Available}",
                label,
                model,
                Truncate(endpoint, 40),
                string.Join(", ", availableModels.Take(10))
            );
            errors++;
            return;
        }

        log.Information("LLM {Label}: {Model} @ {Endpoint}", label, model, Truncate(endpoint, 40));
    }

    private static void CheckLive2D(ILogger log, IConfiguration config, ref int warnings)
    {
        var modelPath = config["Config:Live2D:ModelPath"] ?? "Resources/Live2D/Avatars";
        var modelName = config["Config:Live2D:ModelName"] ?? "aria";
        var fullPath = Path.Combine(Directory.GetCurrentDirectory(), modelPath, modelName);

        if (Directory.Exists(fullPath))
        {
            log.Information("Live2D: Avatar '{ModelName}' found", modelName);
        }
        else
        {
            log.Warning(
                "Live2D: Avatar directory not found at {Path}/{ModelName}. Place your Live2D model in the correct directory. See Live2D.md",
                modelPath,
                modelName
            );
            warnings++;
        }
    }

    private static void CheckPrompt(ILogger log, IConfiguration config, ref int warnings)
    {
        var promptFile = config["Config:ConversationContext:SystemPromptFile"] ?? "personality.txt";
        var fullPath = Path.Combine(
            Directory.GetCurrentDirectory(),
            "Resources",
            "Prompts",
            promptFile
        );

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
