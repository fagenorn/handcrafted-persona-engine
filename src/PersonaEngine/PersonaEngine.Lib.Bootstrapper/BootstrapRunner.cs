using Microsoft.Extensions.Logging;
using PersonaEngine.Lib.Assets;
using PersonaEngine.Lib.Assets.Manifest;
using PersonaEngine.Lib.Bootstrapper.GpuPreflight;
using PersonaEngine.Lib.Bootstrapper.Planner;
using PersonaEngine.Lib.Bootstrapper.Sources;

namespace PersonaEngine.Lib.Bootstrapper;

/// <summary>
/// Orchestrates a full bootstrap run: read lock → resolve profile → compute plan → download → write lock.
/// Catches generic exceptions and reports them via <see cref="BootstrapResult"/>.
/// </summary>
public sealed class BootstrapRunner
{
    private readonly InstallManifest _manifest;
    private readonly InstallStateLockStore _lockStore;
    private readonly AssetPlanner _planner;
    private readonly IAssetDownloader _downloader;

    // Kept for DI shape and future use. IAssetCatalog auto-refreshes via FileSystemWatcher;
    // no explicit refresh call is needed after a download run.
    private readonly IAssetCatalog _catalog;
    private readonly IBootstrapUserInterface _ui;
    private readonly IGpuPreflightCheck _gpuPreflight;
    private readonly string _resourceRoot;
    private readonly TimeProvider _time;
    private readonly ILogger<BootstrapRunner>? _log;

    public BootstrapRunner(
        InstallManifest manifest,
        InstallStateLockStore lockStore,
        AssetPlanner planner,
        IAssetDownloader downloader,
        IAssetCatalog catalog,
        IBootstrapUserInterface ui,
        IGpuPreflightCheck gpuPreflight,
        string resourceRoot,
        TimeProvider? time = null,
        ILogger<BootstrapRunner>? log = null
    )
    {
        _manifest = manifest;
        _lockStore = lockStore;
        _planner = planner;
        _downloader = downloader;
        _catalog = catalog;
        _ui = ui;
        _gpuPreflight = gpuPreflight;
        _resourceRoot = resourceRoot;
        _time = time ?? TimeProvider.System;
        _log = log;
    }

    public async Task<BootstrapResult> RunAsync(BootstrapOptions options, CancellationToken ct)
    {
        try
        {
            // GPU preflight runs first so a user with an incompatible rig doesn't
            // spend hours downloading ~9 GB of CUDA/cuDNN/ONNX redistributables.
            // The check is opt-out via --skip-gpu-check for dev / WSL / remote-GPU
            // scenarios where nvidia-smi may report oddly but CUDA still works.
            if (!options.SkipGpuCheck)
            {
                var gpu = await _gpuPreflight.InspectAsync(ct).ConfigureAwait(false);
                if (!gpu.Ok)
                {
                    _log?.LogWarning(
                        "GPU preflight failed: {Kind} — {Detail}",
                        gpu.Kind,
                        gpu.Detail
                    );
                    var response = await _ui.ShowGpuWarningAsync(gpu, ct).ConfigureAwait(false);
                    if (response == GpuWarningResponse.Abort)
                    {
                        return BootstrapResult.Fail(
                            options.PreselectedProfile ?? ProfileTier.TryItOut,
                            $"GPU preflight check failed ({gpu.Kind}): {gpu.Detail}"
                        );
                    }
                    _log?.LogInformation(
                        "User chose to continue despite GPU preflight failure ({Kind})",
                        gpu.Kind
                    );
                }
            }

            var existingLock = _lockStore.Read(_manifest.ManifestVersion);
            var profile = await ResolveProfileAsync(options, existingLock, ct)
                .ConfigureAwait(false);

            var plan = _planner.Compute(_manifest, existingLock, profile, options.Mode);

            if (plan.IsEmpty)
            {
                return BootstrapResult.Ok(profile, changesApplied: false);
            }

            if (options.Mode == BootstrapMode.Offline)
            {
                // Only report non-Skip items as missing — Skip means already installed.
                var missing = string.Join(
                    ", ",
                    plan.Items.Where(i => i.Action != AssetAction.Skip).Select(i => i.Entry.Id)
                );
                return BootstrapResult.Fail(
                    profile,
                    $"Offline mode: required assets are not installed ({missing})."
                );
            }

            // Only items that require network work are dispatched to the UI/downloader.
            // Reverify is a hash-only check (no download), so it is excluded here;
            // it will be handled in Phase 9 wiring if a dedicated verify pass is added.
            var actionable = plan
                .Items.Where(i => i.Action is AssetAction.Download or AssetAction.Redownload)
                .ToList();

            if (actionable.Count == 0)
            {
                // Plan was non-empty (e.g. only Remove/Reverify items) — nothing to download.
                ApplyRemovals(plan);
                var newLockNoDownload = BuildUpdatedLock(
                    _manifest,
                    existingLock,
                    plan,
                    profile,
                    _time
                );
                _lockStore.Write(newLockNoDownload);
                return BootstrapResult.Ok(
                    profile,
                    changesApplied: plan.Items.Any(i => i.Action == AssetAction.Remove)
                );
            }

            _ui.ShowPlanSummary(plan);

            var allOk = await _ui.RunWithProgressAsync(
                    actionable,
                    (item, progress, innerCt) => _downloader.DownloadAsync(item, progress, innerCt),
                    ct
                )
                .ConfigureAwait(false);

            if (!allOk)
            {
                return BootstrapResult.Fail(
                    profile,
                    "One or more assets failed to download. See log for details.",
                    changesApplied: true
                );
            }

            ApplyRemovals(plan);
            var newLock = BuildUpdatedLock(_manifest, existingLock, plan, profile, _time);
            _lockStore.Write(newLock);

            var result = BootstrapResult.Ok(profile, changesApplied: true);
            _ui.ShowResult(result);
            return result;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _log?.LogError(ex, "Bootstrap failed");
            return BootstrapResult.Fail(
                options.PreselectedProfile ?? ProfileTier.TryItOut,
                ex.Message
            );
        }
    }

    private async Task<ProfileTier> ResolveProfileAsync(
        BootstrapOptions options,
        InstallStateLock existingLock,
        CancellationToken ct
    )
    {
        if (options.PreselectedProfile is { } pre)
        {
            return pre;
        }

        var lockExists = existingLock.Assets.Count > 0;
        var needsPicker =
            options.Mode == BootstrapMode.Reinstall
            || (options.Mode == BootstrapMode.AutoIfMissing && !lockExists);

        if (needsPicker)
        {
            return await _ui.PickProfileAsync(ProfileChoiceCatalog.BuildFrom(_manifest), ct)
                .ConfigureAwait(false);
        }

        // For Verify/Repair modes called before any install, existingLock.SelectedProfile will be
        // the default (TryItOut). This is a deliberate fallback — no error is raised.
        return existingLock.SelectedProfile;
    }

    /// <summary>
    /// Deletes the on-disk file or directory for every plan item with action
    /// <see cref="AssetAction.Remove" />. The install path is interpreted
    /// relative to <see cref="_resourceRoot" />; absolute paths and any path
    /// that escapes the resource root via <c>..</c> are rejected.
    /// </summary>
    private void ApplyRemovals(AssetPlan plan)
    {
        var fullRoot = Path.GetFullPath(_resourceRoot);

        foreach (var item in plan.Items)
        {
            if (item.Action != AssetAction.Remove)
                continue;

            var installPath = item.Entry.InstallPath;
            if (Path.IsPathRooted(installPath))
            {
                _log?.LogWarning(
                    "Refusing to remove asset '{AssetId}': installPath '{Path}' is absolute",
                    item.Entry.Id,
                    installPath
                );
                continue;
            }

            var target = Path.GetFullPath(Path.Combine(fullRoot, installPath));
            if (
                !target.StartsWith(fullRoot + Path.DirectorySeparatorChar, StringComparison.Ordinal)
                && target != fullRoot
            )
            {
                _log?.LogWarning(
                    "Refusing to remove asset '{AssetId}': resolved path '{Path}' would escape resource root",
                    item.Entry.Id,
                    target
                );
                continue;
            }

            try
            {
                if (File.Exists(target))
                {
                    File.Delete(target);
                    _log?.LogInformation(
                        "Removed obsolete asset file '{AssetId}' at '{Path}'",
                        item.Entry.Id,
                        target
                    );
                }
                else if (Directory.Exists(target))
                {
                    Directory.Delete(target, recursive: true);
                    _log?.LogInformation(
                        "Removed obsolete asset directory '{AssetId}' at '{Path}'",
                        item.Entry.Id,
                        target
                    );
                }
            }
            catch (Exception ex)
            {
                // Removal is best-effort: a locked file shouldn't fail the
                // whole bootstrap. The lock entry is still dropped below so
                // the next run will plan a fresh action against this asset.
                _log?.LogWarning(
                    ex,
                    "Failed to remove asset '{AssetId}' at '{Path}'",
                    item.Entry.Id,
                    target
                );
            }
        }
    }

    private static InstallStateLock BuildUpdatedLock(
        InstallManifest manifest,
        InstallStateLock existingLock,
        AssetPlan plan,
        ProfileTier profile,
        TimeProvider time
    )
    {
        var now = time.GetUtcNow();
        var installed = existingLock.Assets.ToDictionary(
            kvp => kvp.Key,
            kvp => kvp.Value,
            StringComparer.Ordinal
        );

        foreach (var item in plan.Items)
        {
            switch (item.Action)
            {
                case AssetAction.Download:
                case AssetAction.Redownload:
                case AssetAction.Reverify:
                    installed[item.Entry.Id] = new InstalledAssetRecord(
                        Version: item.Entry.Source.SourceVersion,
                        Sha256: item.Entry.Sha256,
                        VerifiedAt: now
                    );
                    break;
                case AssetAction.Remove:
                    // On-disk deletion is handled by ApplyRemovals before this
                    // method runs; here we only drop the lock entry.
                    installed.Remove(item.Entry.Id);
                    break;
                case AssetAction.Skip:
                    // Leave existing entry untouched.
                    break;
            }
        }

        return new InstallStateLock(
            SchemaVersion: 1,
            ManifestVersion: manifest.ManifestVersion,
            InstalledAt: now,
            SelectedProfile: profile,
            Assets: installed,
            UserExclusions: existingLock.UserExclusions
        );
    }
}
