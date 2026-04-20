using Microsoft.ML.OnnxRuntime;

namespace PersonaEngine.Lib.Utils.Onnx;

/// <summary>The hardware execution backend to use for ONNX inference.</summary>
public enum ExecutionProvider
{
    Cpu,
    Cuda,
    CudaWithCpuFallback,
}

/// <summary>Pre-defined thread and execution-mode configurations for ONNX inference sessions.</summary>
public enum SessionProfile
{
    /// <summary>Parallel execution, full CPU core count for both InterOp and IntraOp threads.</summary>
    Default,

    /// <summary>Sequential execution, full CPU core count for both InterOp and IntraOp threads.</summary>
    Sequential,

    /// <summary>Parallel execution, single-threaded (InterOp=1, IntraOp=1). For real-time low-latency inference.</summary>
    LowLatency,

    /// <summary>Sequential execution, half InterOp threads, full IntraOp threads.</summary>
    HalfParallel,
}

/// <summary>
/// Factory for creating ONNX Runtime <see cref="InferenceSession"/> instances and their
/// <see cref="SessionOptions"/> with opinionated defaults for GPU/CPU execution providers
/// and threading profiles.
/// </summary>
public static class OnnxSessionFactory
{
    /// <summary>
    /// Creates and configures a <see cref="SessionOptions"/> instance without opening a model.
    /// Useful for testing or when additional configuration is needed before session creation.
    /// </summary>
    public static SessionOptions CreateOptions(
        ExecutionProvider provider = ExecutionProvider.Cuda,
        SessionProfile profile = SessionProfile.Default,
        OrtLoggingLevel logLevel = OrtLoggingLevel.ORT_LOGGING_LEVEL_ERROR,
        Action<SessionOptions>? configure = null
    )
    {
        var options = new SessionOptions
        {
            EnableMemoryPattern = true,
            GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL,
            LogSeverityLevel = logLevel,
        };

        ApplyProfile(options, profile);
        ApplyExecutionProvider(options, provider);
        configure?.Invoke(options);

        return options;
    }

    /// <summary>
    /// Creates an <see cref="InferenceSession"/> for the model at <paramref name="modelPath"/> using
    /// the specified execution provider, threading profile, and optional extra configuration.
    /// The caller owns the returned session and is responsible for disposing it.
    /// </summary>
    public static InferenceSession Create(
        string modelPath,
        ExecutionProvider provider = ExecutionProvider.Cuda,
        SessionProfile profile = SessionProfile.Default,
        OrtLoggingLevel logLevel = OrtLoggingLevel.ORT_LOGGING_LEVEL_ERROR,
        Action<SessionOptions>? configure = null
    )
    {
        using var options = CreateOptions(provider, profile, logLevel, configure);
        return new InferenceSession(modelPath, options);
    }

    private static void ApplyProfile(SessionOptions options, SessionProfile profile)
    {
        var cpuCount = Environment.ProcessorCount;

        switch (profile)
        {
            case SessionProfile.Default:
                options.ExecutionMode = ExecutionMode.ORT_PARALLEL;
                options.InterOpNumThreads = cpuCount;
                options.IntraOpNumThreads = cpuCount;
                break;
            case SessionProfile.Sequential:
                options.ExecutionMode = ExecutionMode.ORT_SEQUENTIAL;
                options.InterOpNumThreads = cpuCount;
                options.IntraOpNumThreads = cpuCount;
                break;
            case SessionProfile.LowLatency:
                options.ExecutionMode = ExecutionMode.ORT_PARALLEL;
                options.InterOpNumThreads = 1;
                options.IntraOpNumThreads = 1;
                break;
            case SessionProfile.HalfParallel:
                options.ExecutionMode = ExecutionMode.ORT_SEQUENTIAL;
                options.InterOpNumThreads = Math.Max(1, cpuCount / 2);
                options.IntraOpNumThreads = cpuCount;
                break;
            default:
                throw new ArgumentOutOfRangeException(
                    nameof(profile),
                    profile,
                    "Unknown SessionProfile value."
                );
        }
    }

    private static void AppendCuda(SessionOptions options)
    {
        using var cudaOptions = new OrtCUDAProviderOptions();
        // Use DEFAULT algo search to avoid cuDNN Frontend heuristic failures on
        // newer GPU architectures (e.g. Blackwell SM 12.0) where bundled cuDNN
        // lacks execution plans. DEFAULT uses the legacy cuDNN algorithm selection
        // API which works reliably across all architectures.
        cudaOptions.UpdateOptions(
            new Dictionary<string, string> { ["cudnn_conv_algo_search"] = "DEFAULT" }
        );
        options.AppendExecutionProvider_CUDA(cudaOptions);
    }

    private static void ApplyExecutionProvider(SessionOptions options, ExecutionProvider provider)
    {
        switch (provider)
        {
            case ExecutionProvider.Cpu:
                options.AppendExecutionProvider_CPU();
                break;
            case ExecutionProvider.Cuda:
                AppendCuda(options);
                break;
            case ExecutionProvider.CudaWithCpuFallback:
                try
                {
                    AppendCuda(options);
                }
                catch
                {
                    options.AppendExecutionProvider_CPU();
                }
                break;
            default:
                throw new ArgumentOutOfRangeException(
                    nameof(provider),
                    provider,
                    "Unknown ExecutionProvider value."
                );
        }
    }
}
