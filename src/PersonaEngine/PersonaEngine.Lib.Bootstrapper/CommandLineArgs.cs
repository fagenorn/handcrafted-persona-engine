using PersonaEngine.Lib.Bootstrapper.Manifest;

namespace PersonaEngine.Lib.Bootstrapper;

/// <summary>
/// Minimal CLI argument parser for the bootstrapper host.
/// Returns a <see cref="BootstrapOptions"/> and a non-interactive flag.
/// Unknown flags are silently ignored so future flags don't break older hosts.
/// </summary>
public static class CommandLineArgs
{
    private static readonly IReadOnlyDictionary<string, BootstrapMode> _modeFlags = new Dictionary<
        string,
        BootstrapMode
    >(StringComparer.OrdinalIgnoreCase)
    {
        ["--install"] = BootstrapMode.Reinstall,
        ["--verify"] = BootstrapMode.Verify,
        ["--repair"] = BootstrapMode.Repair,
        ["--offline"] = BootstrapMode.Offline,
    };

    private static readonly IReadOnlyDictionary<string, ProfileTier> _profileSlugs = new Dictionary<
        string,
        ProfileTier
    >(StringComparer.OrdinalIgnoreCase)
    {
        ["try-it-out"] = ProfileTier.TryItOut,
        ["stream-with-it"] = ProfileTier.StreamWithIt,
        ["build-with-it"] = ProfileTier.BuildWithIt,
    };

    /// <summary>
    /// Parses <paramref name="args"/> and returns a <see cref="BootstrapOptions"/> and a
    /// <c>nonInteractive</c> flag.
    /// </summary>
    /// <exception cref="ArgumentException">Thrown when an unrecognised profile slug is supplied.</exception>
    public static (BootstrapOptions Options, bool NonInteractive) Parse(IReadOnlyList<string> args)
    {
        var mode = BootstrapMode.AutoIfMissing;
        ProfileTier? preselected = null;
        string? resourceRoot = null;
        var nonInteractive = false;

        for (var i = 0; i < args.Count; i++)
        {
            var arg = args[i];

            if (_modeFlags.TryGetValue(arg, out var mappedMode))
            {
                mode = mappedMode;
                continue;
            }

            if (arg.Equals("--non-interactive", StringComparison.OrdinalIgnoreCase))
            {
                nonInteractive = true;
                continue;
            }

            if (arg.Equals("--profile", StringComparison.OrdinalIgnoreCase))
            {
                if (i + 1 >= args.Count)
                    throw new ArgumentException("--profile requires a value.");

                var slug = args[++i];

                if (!_profileSlugs.TryGetValue(slug, out var tier))
                    throw new ArgumentException(
                        $"Unknown profile slug '{slug}'. Valid values: {string.Join(", ", _profileSlugs.Keys)}."
                    );

                preselected = tier;
                continue;
            }

            if (arg.Equals("--resource-root", StringComparison.OrdinalIgnoreCase))
            {
                if (i + 1 < args.Count)
                    resourceRoot = args[++i];

                continue;
            }

            // Unknown flags are silently ignored for forward-compatibility.
        }

        var options = new BootstrapOptions
        {
            Mode = mode,
            PreselectedProfile = preselected,
            ResourceRootOverride = resourceRoot,
        };

        return (options, nonInteractive);
    }
}
