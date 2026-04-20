using PersonaEngine.Lib.Assets.Manifest;

namespace PersonaEngine.Lib.Bootstrapper;

public sealed record CommandLineArgs
{
    public required BootstrapOptions Bootstrap { get; init; }
    public required bool NonInteractive { get; init; }

    public static CommandLineArgs Parse(IReadOnlyList<string> args)
    {
        var mode = BootstrapMode.AutoIfMissing;
        ProfileTier? profile = null;
        var nonInteractive = false;
        var skipGpuCheck = false;

        foreach (var arg in args)
        {
            switch (arg)
            {
                case "--reinstall":
                    mode = BootstrapMode.Reinstall;
                    break;
                case "--repair":
                    mode = BootstrapMode.Repair;
                    break;
                case "--verify":
                    mode = BootstrapMode.Verify;
                    break;
                case "--offline":
                    mode = BootstrapMode.Offline;
                    break;
                case "--non-interactive":
                    nonInteractive = true;
                    break;
                case "--skip-gpu-check":
                    skipGpuCheck = true;
                    break;
                case var s when s.StartsWith("--profile=", StringComparison.Ordinal):
                    profile = ParseProfile(s.Substring("--profile=".Length));
                    break;
                default:
                    // Unknown args are silently ignored — the bootstrapper has
                    // no callees that need them. If a future subsystem needs
                    // pass-through args, restore an explicit PassThrough list
                    // and forward it from Program.Main.
                    break;
            }
        }

        return new CommandLineArgs
        {
            Bootstrap = new BootstrapOptions
            {
                Mode = mode,
                PreselectedProfile = profile,
                SkipGpuCheck = skipGpuCheck,
            },
            NonInteractive = nonInteractive,
        };
    }

    private static ProfileTier ParseProfile(string slug) =>
        slug switch
        {
            "try" => ProfileTier.TryItOut,
            "stream" => ProfileTier.StreamWithIt,
            "build" => ProfileTier.BuildWithIt,
            _ => throw new ArgumentException(
                $"Unknown --profile slug: '{slug}'. Use try|stream|build.",
                nameof(slug)
            ),
        };
}
