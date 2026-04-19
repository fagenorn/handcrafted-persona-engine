using PersonaEngine.Lib.Bootstrapper.Manifest;

namespace PersonaEngine.Lib.Bootstrapper;

public sealed record CommandLineArgs
{
    public required BootstrapOptions Bootstrap { get; init; }
    public required bool NonInteractive { get; init; }
    public required IReadOnlyList<string> PassThrough { get; init; }

    public static CommandLineArgs Parse(IReadOnlyList<string> args)
    {
        var mode = BootstrapMode.AutoIfMissing;
        ProfileTier? profile = null;
        var nonInteractive = false;
        var passThrough = new List<string>();

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
                case var s when s.StartsWith("--profile=", StringComparison.Ordinal):
                    profile = ParseProfile(s.Substring("--profile=".Length));
                    break;
                default:
                    passThrough.Add(arg);
                    break;
            }
        }

        return new CommandLineArgs
        {
            Bootstrap = new BootstrapOptions { Mode = mode, PreselectedProfile = profile },
            NonInteractive = nonInteractive,
            PassThrough = passThrough,
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
