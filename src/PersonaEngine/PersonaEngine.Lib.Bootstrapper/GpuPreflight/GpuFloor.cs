using System.Globalization;

namespace PersonaEngine.Lib.Bootstrapper.GpuPreflight;

/// <summary>
/// Declarative GPU version floors + the parsing/compare helpers that work with them.
/// Separated from <see cref="NvidiaGpuPreflightCheck" /> so the UI layer can read the
/// same constants for guidance messages and tests can exercise version parsing in
/// isolation from the actual probes.
/// </summary>
public static class GpuFloor
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

    /// <summary>
    /// Returns negative / zero / positive like <see cref="IComparable" />.
    /// Compares major then minor — patch digits on driver strings (e.g. the
    /// trailing ".01" in "580.65.01") are stripped by <see cref="TryParseVersion" />.
    /// </summary>
    public static int CompareVersion((int Major, int Minor) a, (int Major, int Minor) b) =>
        a.Major != b.Major ? a.Major.CompareTo(b.Major) : a.Minor.CompareTo(b.Minor);

    /// <summary>
    /// Parses "&lt;major&gt;[.&lt;minor&gt;[.&lt;patch&gt;...]]" loosely — we only care about the first
    /// two components. nvidia-smi driver_version is typically "580.65.01" on
    /// Linux and "580.65" on Windows; compute_cap is always "X.Y".
    /// </summary>
    public static (int Major, int Minor)? TryParseVersion(string? s)
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
