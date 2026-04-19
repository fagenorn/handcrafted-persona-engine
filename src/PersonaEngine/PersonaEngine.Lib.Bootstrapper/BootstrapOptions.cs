using PersonaEngine.Lib.Bootstrapper.Manifest;

namespace PersonaEngine.Lib.Bootstrapper;

/// <summary>Inputs to a bootstrap run, parsed from CLI args by the host.</summary>
public sealed record BootstrapOptions
{
    public BootstrapMode Mode { get; init; } = BootstrapMode.AutoIfMissing;
    public ProfileTier? PreselectedProfile { get; init; }
    public string? ResourceRootOverride { get; init; }
}
