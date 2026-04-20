using PersonaEngine.Lib.Assets.Manifest;

namespace PersonaEngine.Lib.Bootstrapper.Sources;

public interface IAssetSource
{
    SourceType Type { get; }

    /// <summary>
    ///     Resolves the download URL + post-processing for an asset entry.
    /// </summary>
    /// <param name="asset">Manifest entry being installed.</param>
    /// <param name="resolvedInstallPath">
    ///     Absolute install path the caller has already resolved against the
    ///     resource root. Source clients pass this into <see cref="ZipExtractor"/>
    ///     as the extraction target so extraction is anchored to the resource
    ///     root, not the current working directory. <see cref="AssetEntry.InstallPath"/>
    ///     itself is relative and must not be passed to <see cref="ZipExtractor"/>
    ///     directly — doing so would resolve against <c>Environment.CurrentDirectory</c>.
    /// </param>
    /// <param name="ct">Cancellation token.</param>
    Task<AssetDownload> ResolveAsync(
        AssetEntry asset,
        string resolvedInstallPath,
        CancellationToken ct
    );
}
