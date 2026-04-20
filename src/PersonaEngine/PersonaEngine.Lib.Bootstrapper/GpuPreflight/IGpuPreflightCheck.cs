namespace PersonaEngine.Lib.Bootstrapper.GpuPreflight;

/// <summary>
/// Runs a best-effort inspection of the local NVIDIA stack before the installer
/// spends bandwidth downloading ~9 GB of CUDA / cuDNN / ONNX redistributables.
/// Implementations must never throw: environmental probes are considered part
/// of the check surface and get reported as a <see cref="GpuStatus" /> failure.
/// </summary>
public interface IGpuPreflightCheck
{
    Task<GpuStatus> InspectAsync(CancellationToken ct);
}

/// <summary>Structured result of a single nvidia-smi invocation.</summary>
public sealed record NvidiaSmiResult(string DriverVersion, string GpuName, string? ComputeCap);

/// <summary>
/// Wraps the nvidia-smi process probe so <see cref="NvidiaGpuPreflightCheck" />
/// is testable without spawning external processes.
/// </summary>
public interface INvidiaSmiRunner
{
    /// <summary>Returns null if nvidia-smi isn't installed / failed / timed out.</summary>
    Task<NvidiaSmiResult?> QueryAsync(CancellationToken ct);
}

/// <summary>
/// Wraps the <c>LoadLibrary("nvcuda.dll")</c> fallback so it can be faked in tests.
/// Only meaningful on Windows; non-Windows hosts will always see <c>false</c>.
/// </summary>
public interface INvcudaProbe
{
    bool TryLoadNvcuda();
}
