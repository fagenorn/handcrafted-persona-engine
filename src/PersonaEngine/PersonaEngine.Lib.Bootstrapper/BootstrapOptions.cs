using PersonaEngine.Lib.Assets.Manifest;

namespace PersonaEngine.Lib.Bootstrapper;

/// <summary>Inputs to a bootstrap run, parsed from CLI args by the host.</summary>
public sealed record BootstrapOptions
{
    public BootstrapMode Mode { get; init; } = BootstrapMode.AutoIfMissing;

    /// <summary>If non-null in Reinstall/AutoIfMissing-with-no-lock paths, skip the picker and use this profile.</summary>
    public ProfileTier? PreselectedProfile { get; init; }

    /// <summary>Override resource root (defaults to AppContext.BaseDirectory + "Resources").</summary>
    /// <remarks>
    /// Consumed by host wiring (Phase 9), not by <see cref="BootstrapRunner"/> directly.
    /// It will be passed to <c>PlanItemAssetDownloader</c> and <c>AssetCatalog</c> constructors
    /// during app startup.
    /// </remarks>
    public string? ResourceRootOverride { get; init; }
}
