using System.Globalization;
using Microsoft.Extensions.Logging;

namespace PersonaEngine.Lib.Bootstrapper.GpuPreflight;

/// <summary>
/// Composes <see cref="INvidiaSmiRunner" /> + <see cref="INvcudaProbe" /> into a
/// preflight decision. The actual version floors come from the shipping
/// install-manifest.json: we bundle CUDA 13.0.3 (requires Windows driver
/// ≥ 580.65) and cuDNN 9.1.1 (requires a cuBLAS / cuBLASLt that matches), and
/// ONNX Runtime's GPU EP plus cuDNN 9 drop support below compute 6.0 (Pascal).
/// </summary>
public sealed class NvidiaGpuPreflightCheck : IGpuPreflightCheck
{
    /// <summary>
    /// Windows driver ≥ 580.65 is the documented floor for CUDA 13.0.x user-mode
    /// libraries. 12.x libraries are forward-compatible under the same driver,
    /// so this one floor covers both runtimes we ship.
    /// </summary>
    public static readonly (int Major, int Minor) MinDriver = (580, 65);

    /// <summary>
    /// Pascal (compute 6.0) is the oldest architecture still covered by modern
    /// cuDNN 9 and ONNX Runtime's CUDA EP. Maxwell (5.x) works for some ops but
    /// is not supported, so we block below this floor.
    /// </summary>
    public static readonly (int Major, int Minor) MinCompute = (6, 0);

    private readonly INvidiaSmiRunner _smi;
    private readonly INvcudaProbe _nvcuda;
    private readonly ILogger<NvidiaGpuPreflightCheck>? _log;

    public NvidiaGpuPreflightCheck(
        INvidiaSmiRunner smi,
        INvcudaProbe nvcuda,
        ILogger<NvidiaGpuPreflightCheck>? log = null
    )
    {
        _smi = smi;
        _nvcuda = nvcuda;
        _log = log;
    }

    public async Task<GpuStatus> InspectAsync(CancellationToken ct)
    {
        var smi = await _smi.QueryAsync(ct).ConfigureAwait(false);
        if (smi is null)
        {
            // nvidia-smi absent or unusable. Fall back to probing nvcuda.dll so
            // we don't reject stripped system images where the CUDA user-mode
            // driver is installed but the SMI CLI isn't on PATH (uncommon but
            // happens on some cloud images and WSL mounts). If it loads we only
            // know "some NVIDIA driver is present" — not enough to verify
            // version, so we still warn the user and let them decide.
            if (_nvcuda.TryLoadNvcuda())
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

        var driverParsed = TryParseVersion(smi.DriverVersion);
        if (driverParsed is null)
        {
            _log?.LogWarning(
                "nvidia-smi reported unparseable driver version '{Version}' — proceeding with warning",
                smi.DriverVersion
            );
            return GpuStatus.Fail(
                GpuFailureKind.UnknownGpu,
                $"Couldn't parse driver version '{smi.DriverVersion}' reported by nvidia-smi. "
                    + "This may be fine, but we can't confirm you meet the minimum.",
                driver: smi.DriverVersion,
                name: smi.GpuName
            );
        }

        if (CompareVersion(driverParsed.Value, MinDriver) < 0)
        {
            return GpuStatus.Fail(
                GpuFailureKind.DriverTooOld,
                $"Driver {smi.DriverVersion} is older than the required {MinDriver.Major}.{MinDriver.Minor:00} "
                    + "(needed for the CUDA 13 runtime we bundle). Update from nvidia.com.",
                driver: smi.DriverVersion,
                name: smi.GpuName
            );
        }

        (int Major, int Minor)? cc = null;
        if (!string.IsNullOrWhiteSpace(smi.ComputeCap))
        {
            cc = TryParseVersion(smi.ComputeCap);
            if (cc is null)
            {
                _log?.LogDebug(
                    "nvidia-smi reported unparseable compute_cap '{Cap}' — ignoring",
                    smi.ComputeCap
                );
            }
            else if (CompareVersion(cc.Value, MinCompute) < 0)
            {
                return GpuStatus.Fail(
                    GpuFailureKind.ComputeTooLow,
                    $"GPU '{smi.GpuName}' has compute capability {cc.Value.Major}.{cc.Value.Minor}, "
                        + $"below the {MinCompute.Major}.{MinCompute.Minor} floor (Pascal). "
                        + "Persona Engine won't run reliably on this card.",
                    driver: smi.DriverVersion,
                    name: smi.GpuName,
                    cc: cc
                );
            }
        }

        return GpuStatus.Pass(smi.DriverVersion, smi.GpuName, cc);
    }

    /// <summary>
    /// Returns negative / zero / positive like <see cref="IComparable" />.
    /// Compares major then minor — patch digits on driver strings (e.g. the
    /// trailing ".01" in "580.65.01") are stripped by <see cref="TryParseVersion" />.
    /// </summary>
    private static int CompareVersion((int Major, int Minor) a, (int Major, int Minor) b)
    {
        if (a.Major != b.Major)
            return a.Major.CompareTo(b.Major);
        return a.Minor.CompareTo(b.Minor);
    }

    /// <summary>
    /// Parses "&lt;major&gt;[.&lt;minor&gt;[.&lt;patch&gt;...]]" loosely — we only care about the first
    /// two components. nvidia-smi driver_version is typically "580.65.01" on
    /// Linux and "580.65" on Windows; compute_cap is always "X.Y".
    /// </summary>
    internal static (int Major, int Minor)? TryParseVersion(string? s)
    {
        if (string.IsNullOrWhiteSpace(s))
            return null;

        var trimmed = s.Trim();
        var parts = trimmed.Split('.');
        if (
            !int.TryParse(
                parts[0],
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out var major
            )
        )
            return null;

        var minor = 0;
        if (parts.Length > 1)
        {
            // Accept "65" or "65b2" (shouldn't happen, but be forgiving) —
            // take the leading digit run of the minor part.
            var minorPart = parts[1];
            var i = 0;
            while (i < minorPart.Length && char.IsDigit(minorPart[i]))
                i++;
            if (i == 0)
                return null;
            minor = int.Parse(
                minorPart.AsSpan(0, i),
                NumberStyles.Integer,
                CultureInfo.InvariantCulture
            );
        }

        return (major, minor);
    }
}
