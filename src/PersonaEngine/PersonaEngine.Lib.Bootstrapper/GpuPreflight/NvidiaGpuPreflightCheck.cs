using Microsoft.Extensions.Logging;

namespace PersonaEngine.Lib.Bootstrapper.GpuPreflight;

/// <summary>
/// Composes <see cref="INvidiaSmiRunner" /> + <see cref="INvcudaProbe" /> into a
/// preflight decision. The actual version floors live in <see cref="GpuFloor" />
/// so the UI layer and tests can read them without pulling in the probes.
/// </summary>
public sealed class NvidiaGpuPreflightCheck(
    INvidiaSmiRunner smi,
    INvcudaProbe nvcuda,
    ILogger<NvidiaGpuPreflightCheck>? log = null
) : IGpuPreflightCheck
{
    // Re-exposed for call sites that already reference these (e.g. UI guidance
    // text, existing tests). New code should prefer GpuFloor directly.
    public static readonly (int Major, int Minor) MinDriver = GpuFloor.MinDriver;
    public static readonly (int Major, int Minor) MinCompute = GpuFloor.MinCompute;

    public async Task<GpuStatus> InspectAsync(CancellationToken ct)
    {
        var smiResult = await smi.QueryAsync(ct).ConfigureAwait(false);
        if (smiResult is null)
        {
            // nvidia-smi absent or unusable. Fall back to probing nvcuda.dll so
            // we don't reject stripped system images where the CUDA user-mode
            // driver is installed but the SMI CLI isn't on PATH (uncommon but
            // happens on some cloud images and WSL mounts). If it loads we only
            // know "some NVIDIA driver is present" — not enough to verify
            // version, so we still warn the user and let them decide.
            if (nvcuda.TryLoadNvcuda())
            {
                return GpuStatus.Fail(
                    GpuFailureKind.ToolMissing,
                    "NVIDIA driver is loaded, but the `nvidia-smi` tool isn't on PATH — "
                        + "we can't verify the driver version or GPU model."
                );
            }

            return GpuStatus.Fail(
                GpuFailureKind.NoDriver,
                "No NVIDIA driver detected. Persona Engine needs an NVIDIA GPU with CUDA support. "
                    + "Install the latest GeForce or Studio driver from nvidia.com."
            );
        }

        var driverParsed = GpuFloor.TryParseVersion(smiResult.DriverVersion);
        if (driverParsed is null)
        {
            log?.LogWarning(
                "nvidia-smi reported unparseable driver version '{Version}' — proceeding with warning",
                smiResult.DriverVersion
            );
            return GpuStatus.Fail(
                GpuFailureKind.UnknownGpu,
                $"Couldn't parse driver version '{smiResult.DriverVersion}' reported by nvidia-smi. "
                    + "This may be fine, but we can't confirm you meet the minimum.",
                driver: smiResult.DriverVersion,
                name: smiResult.GpuName
            );
        }

        if (GpuFloor.CompareVersion(driverParsed.Value, GpuFloor.MinDriver) < 0)
        {
            return GpuStatus.Fail(
                GpuFailureKind.DriverTooOld,
                $"Driver {smiResult.DriverVersion} is older than the required {GpuFloor.MinDriver.Major}.{GpuFloor.MinDriver.Minor:00} "
                    + "(needed for the CUDA 13 runtime we bundle). Update from nvidia.com.",
                driver: smiResult.DriverVersion,
                name: smiResult.GpuName
            );
        }

        (int Major, int Minor)? cc = null;
        if (!string.IsNullOrWhiteSpace(smiResult.ComputeCap))
        {
            cc = GpuFloor.TryParseVersion(smiResult.ComputeCap);
            if (cc is null)
            {
                log?.LogDebug(
                    "nvidia-smi reported unparseable compute_cap '{Cap}' — ignoring",
                    smiResult.ComputeCap
                );
            }
            else if (GpuFloor.CompareVersion(cc.Value, GpuFloor.MinCompute) < 0)
            {
                return GpuStatus.Fail(
                    GpuFailureKind.ComputeTooLow,
                    $"GPU '{smiResult.GpuName}' has compute capability {cc.Value.Major}.{cc.Value.Minor}, "
                        + $"below the {GpuFloor.MinCompute.Major}.{GpuFloor.MinCompute.Minor} floor (Pascal). "
                        + "Persona Engine won't run reliably on this card.",
                    driver: smiResult.DriverVersion,
                    name: smiResult.GpuName,
                    cc: cc
                );
            }
        }

        return GpuStatus.Pass(smiResult.DriverVersion, smiResult.GpuName, cc);
    }

    // Kept for backward compat with the existing test suite that calls
    // NvidiaGpuPreflightCheck.TryParseVersion directly. New callers should use
    // GpuFloor.TryParseVersion.
    internal static (int Major, int Minor)? TryParseVersion(string? s) =>
        GpuFloor.TryParseVersion(s);
}
