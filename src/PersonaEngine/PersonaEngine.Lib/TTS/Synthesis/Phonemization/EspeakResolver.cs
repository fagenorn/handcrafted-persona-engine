namespace PersonaEngine.Lib.TTS.Synthesis;

/// <summary>
///     Resolves how to invoke espeak-ng with a "bundled-first" policy.
///     Release builds ship a portable espeak-ng under
///     <c>{AppContext.BaseDirectory}/native/espeak/</c> so the app can run
///     without a system install. If that bundled binary exists it wins,
///     and we hand back the sibling <c>espeak-ng-data</c> directory so the
///     caller can pass <c>--path</c> (without it, espeak-ng falls back to its
///     compile-time default of <c>C:\Program Files\eSpeak NG\espeak-ng-data</c>
///     which doesn't exist on a fresh machine).
///     If no bundle is present, the configured value is returned unchanged so
///     the OS resolves it via PATH (or honours an absolute override).
/// </summary>
public static class EspeakResolver
{
    private const string BundledDirRelativePath = "native/espeak";

    public readonly record struct Resolution(string ExecutablePath, string? DataPath)
    {
        public bool IsBundled => DataPath is not null;
    }

    public static Resolution Resolve(string? configuredPath)
    {
        var bundledDir = Path.Combine(
            AppContext.BaseDirectory,
            BundledDirRelativePath.Replace('/', Path.DirectorySeparatorChar)
        );
        var bundledExe = Path.Combine(bundledDir, "espeak-ng.exe");
        var bundledDataDir = Path.Combine(bundledDir, "espeak-ng-data");

        if (File.Exists(bundledExe) && Directory.Exists(bundledDataDir))
        {
            return new Resolution(bundledExe, bundledDataDir);
        }

        var fallbackExe = string.IsNullOrWhiteSpace(configuredPath) ? "espeak-ng" : configuredPath;

        return new Resolution(fallbackExe, null);
    }
}
