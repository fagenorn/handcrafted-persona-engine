namespace PersonaEngine.Lib.Bootstrapper.GpuPreflight;

/// <summary>
/// Why a GPU preflight check failed, or <see cref="None" /> for a pass.
/// The installer maps this to a user-facing message and to a decision about
/// whether to continue or abort.
/// </summary>
public enum GpuFailureKind
{
    /// <summary>Preflight passed. No action needed.</summary>
    None,

    /// <summary>No NVIDIA driver at all — nvidia-smi missing and nvcuda.dll didn't load.</summary>
    NoDriver,

    /// <summary>NVIDIA driver is present but older than the minimum we need for CUDA 13.</summary>
    DriverTooOld,

    /// <summary>GPU compute capability is below the supported floor (Pascal 6.0).</summary>
    ComputeTooLow,

    /// <summary>nvidia-smi returned output we couldn't parse. Treated as "warn-and-continue".</summary>
    UnknownGpu,

    /// <summary>nvidia-smi isn't on PATH but nvcuda.dll loaded. Driver is likely fine; we just can't verify.</summary>
    ToolMissing,
}

/// <summary>
/// Structured result of a single preflight probe. Immutable snapshot so it can
/// be passed through UI layers without fear of mutation.
/// </summary>
public sealed record GpuStatus
{
    public required bool Ok { get; init; }
    public required GpuFailureKind Kind { get; init; }

    /// <summary>Raw driver version string as reported by nvidia-smi (e.g. "580.65.01").</summary>
    public string? DriverVersion { get; init; }

    /// <summary>GPU marketing name as reported by nvidia-smi (e.g. "NVIDIA GeForce RTX 4080").</summary>
    public string? GpuName { get; init; }

    /// <summary>Parsed compute capability; null if nvidia-smi didn't report one or we couldn't parse it.</summary>
    public (int Major, int Minor)? ComputeCapability { get; init; }

    /// <summary>Human-readable explanation shown to the user on failure.</summary>
    public string? Detail { get; init; }

    public static GpuStatus Pass(string? driver, string? name, (int Major, int Minor)? cc) =>
        new()
        {
            Ok = true,
            Kind = GpuFailureKind.None,
            DriverVersion = driver,
            GpuName = name,
            ComputeCapability = cc,
        };

    public static GpuStatus Fail(
        GpuFailureKind kind,
        string detail,
        string? driver = null,
        string? name = null,
        (int Major, int Minor)? cc = null
    ) =>
        new()
        {
            Ok = false,
            Kind = kind,
            Detail = detail,
            DriverVersion = driver,
            GpuName = name,
            ComputeCapability = cc,
        };
}
