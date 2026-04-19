using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using PersonaEngine.Lib.Assets;
using PersonaEngine.Lib.Bootstrapper.Manifest;
using PersonaEngine.Lib.Bootstrapper.Planner;
using PersonaEngine.Lib.Bootstrapper.Sources;

namespace PersonaEngine.Lib.Bootstrapper;

/// <summary>
/// Orchestrates a full bootstrap run: load lock → pick profile → compute plan →
/// confirm with user → download assets → write updated lock → return result.
/// </summary>
public sealed class BootstrapRunner
{
    private readonly InstallManifest _manifest;
    private readonly InstallStateLockStore _lockStore;
    private readonly AssetPlanner _planner;
    private readonly IAssetDownloader _downloader;
    private readonly IAssetCatalog _catalog;
    private readonly IBootstrapUserInterface _ui;
    private readonly string _resourceRoot;
    private readonly ILogger<BootstrapRunner> _log;

    public BootstrapRunner(
        InstallManifest manifest,
        InstallStateLockStore lockStore,
        AssetPlanner planner,
        IAssetDownloader downloader,
        IAssetCatalog catalog,
        IBootstrapUserInterface ui,
        string resourceRoot,
        ILogger<BootstrapRunner>? log = null
    )
    {
        _manifest = manifest;
        _lockStore = lockStore;
        _planner = planner;
        _downloader = downloader;
        _catalog = catalog;
        _ui = ui;
        _resourceRoot = resourceRoot;
        _log = log ?? NullLogger<BootstrapRunner>.Instance;
    }

    public async Task<BootstrapResult> RunAsync(BootstrapOptions options, CancellationToken ct)
    {
        _log.LogInformation("Bootstrap run starting. Mode={Mode}", options.Mode);

        // 1. Load current install lock (empty if first run).
        var lockState = _lockStore.Read(_manifest.ManifestVersion);

        // 2. Determine profile tier.
        ProfileTier profile;
        if (options.PreselectedProfile.HasValue)
        {
            // Caller (CLI) pre-selected a profile — no picker needed.
            profile = options.PreselectedProfile.Value;
        }
        else if (lockState.Assets.Count > 0)
        {
            // An existing installation: use the previously-chosen profile.
            profile = lockState.SelectedProfile;
        }
        else
        {
            // First-time run: ask the user to choose.
            var choice = await _ui.PickProfileAsync(ct).ConfigureAwait(false);
            if (choice is null)
            {
                _log.LogInformation("User cancelled profile selection. Aborting bootstrap.");
                return new BootstrapResult
                {
                    Success = false,
                    ActiveProfile = ProfileTier.TryItOut,
                    ChangesApplied = false,
                    ErrorMessage = "Profile selection cancelled by user.",
                };
            }

            profile = choice.Tier;

            // Merge user exclusions into the lock so the planner honours them.
            lockState = lockState with
            {
                SelectedProfile = profile,
                UserExclusions = choice.ExcludedAssetIds,
            };
        }

        // 3. Compute plan.
        var plan = _planner.Compute(_manifest, lockState, profile, options.Mode);

        if (plan.IsEmpty)
        {
            _log.LogInformation("All assets up-to-date. Nothing to do.");
            await _ui.ShowResultAsync(
                    new BootstrapResult
                    {
                        Success = true,
                        ActiveProfile = profile,
                        ChangesApplied = false,
                    },
                    ct
                )
                .ConfigureAwait(false);
            return new BootstrapResult
            {
                Success = true,
                ActiveProfile = profile,
                ChangesApplied = false,
            };
        }

        // 4. Confirm plan with user.
        var confirmed = await _ui.ConfirmPlanAsync(plan, ct).ConfigureAwait(false);
        if (!confirmed)
        {
            _log.LogInformation("User declined the download plan. Aborting bootstrap.");
            return new BootstrapResult
            {
                Success = false,
                ActiveProfile = profile,
                ChangesApplied = false,
                ErrorMessage = "Download plan rejected by user.",
            };
        }

        // 5. Execute downloads.
        var warnings = new List<string>();
        var installedAssets = new Dictionary<string, InstalledAssetRecord>(
            lockState.Assets,
            StringComparer.Ordinal
        );

        foreach (var item in plan.Items)
        {
            ct.ThrowIfCancellationRequested();

            if (item.Action is AssetAction.Skip)
                continue;

            if (item.Action is AssetAction.Remove)
            {
                var assetPath = Path.Combine(_resourceRoot, item.Entry.InstallPath);
                try
                {
                    if (File.Exists(assetPath))
                        File.Delete(assetPath);
                    else if (Directory.Exists(assetPath))
                        Directory.Delete(assetPath, recursive: true);

                    installedAssets.Remove(item.Entry.Id);
                    _log.LogDebug("Removed asset '{AssetId}'", item.Entry.Id);
                }
                catch (Exception ex)
                {
                    _log.LogWarning(ex, "Failed to remove asset '{AssetId}'", item.Entry.Id);
                    warnings.Add($"Could not remove '{item.Entry.Id}': {ex.Message}");
                }
                continue;
            }

            // Download or Redownload or Reverify.
            _log.LogInformation(
                "Processing asset '{AssetId}' (action={Action})",
                item.Entry.Id,
                item.Action
            );

            try
            {
                var progressReporter = new Progress<long>(bytes =>
                    _ui.ReportProgress(item, bytes, item.Entry.SizeBytes)
                );

                await _downloader.DownloadAsync(item, progressReporter, ct).ConfigureAwait(false);

                installedAssets[item.Entry.Id] = new InstalledAssetRecord(
                    item.Entry.Source.SourceVersion,
                    item.Entry.Sha256,
                    DateTimeOffset.UtcNow
                );
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Failed to process asset '{AssetId}'", item.Entry.Id);
                var result = new BootstrapResult
                {
                    Success = false,
                    ActiveProfile = profile,
                    ChangesApplied = installedAssets.Count > lockState.Assets.Count,
                    ErrorMessage = $"Failed to process '{item.Entry.Id}': {ex.Message}",
                    Warnings = warnings,
                };
                await _ui.ShowResultAsync(result, ct).ConfigureAwait(false);
                return result;
            }
        }

        // 6. Persist updated lock.
        var updatedLock = lockState with
        {
            ManifestVersion = _manifest.ManifestVersion,
            InstalledAt = DateTimeOffset.UtcNow,
            SelectedProfile = profile,
            Assets = installedAssets,
        };
        _lockStore.Write(updatedLock);
        _log.LogInformation("Install lock updated. Profile={Profile}", profile);

        var successResult = new BootstrapResult
        {
            Success = true,
            ActiveProfile = profile,
            ChangesApplied = true,
            Warnings = warnings,
        };
        await _ui.ShowResultAsync(successResult, ct).ConfigureAwait(false);
        return successResult;
    }
}
