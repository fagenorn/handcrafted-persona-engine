using Microsoft.Extensions.Logging;
using PersonaEngine.Lib.Assets;
using PersonaEngine.Lib.Bootstrapper.Manifest;
using PersonaEngine.Lib.Bootstrapper.Planner;
using PersonaEngine.Lib.Bootstrapper.Sources;

namespace PersonaEngine.Lib.Bootstrapper;

public sealed class BootstrapRunner
{
    private readonly InstallManifest _manifest;
    private readonly InstallStateLockStore _lockStore;
    private readonly AssetPlanner _planner;
    private readonly IAssetDownloader _downloader;
    private readonly IAssetCatalog _catalog;
    private readonly IBootstrapUserInterface _ui;
    private readonly ILogger<BootstrapRunner>? _log;

    public BootstrapRunner(
        InstallManifest manifest,
        InstallStateLockStore lockStore,
        AssetPlanner planner,
        IAssetDownloader downloader,
        IAssetCatalog catalog,
        IBootstrapUserInterface ui,
        ILogger<BootstrapRunner>? log = null
    )
    {
        _manifest = manifest;
        _lockStore = lockStore;
        _planner = planner;
        _downloader = downloader;
        _catalog = catalog;
        _ui = ui;
        _log = log;
    }

    public async Task<BootstrapResult> RunAsync(BootstrapOptions options, CancellationToken ct)
    {
        try
        {
            var existingLock = _lockStore.Read(_manifest.ManifestVersion);
            var profile = await ResolveProfileAsync(options, existingLock, ct)
                .ConfigureAwait(false);

            var plan = _planner.Compute(_manifest, existingLock, profile, options.Mode);

            if (plan.IsEmpty)
            {
                return new BootstrapResult
                {
                    Success = true,
                    ActiveProfile = profile,
                    ChangesApplied = false,
                };
            }

            if (options.Mode == BootstrapMode.Offline)
            {
                var missing = string.Join(", ", plan.Items.Select(i => i.Entry.Id));
                return new BootstrapResult
                {
                    Success = false,
                    ActiveProfile = profile,
                    ChangesApplied = false,
                    ErrorMessage = $"Offline mode: required assets are not installed ({missing}).",
                };
            }

            _ui.ShowPlanSummary(plan);

            var allOk = await _ui.RunWithProgressAsync(
                    plan.Items,
                    (item, progress, innerCt) => _downloader.DownloadAsync(item, progress, innerCt),
                    ct
                )
                .ConfigureAwait(false);

            if (!allOk)
            {
                return new BootstrapResult
                {
                    Success = false,
                    ActiveProfile = profile,
                    ChangesApplied = true,
                    ErrorMessage = "One or more assets failed to download. See log for details.",
                };
            }

            var newLock = BuildUpdatedLock(_manifest, existingLock, plan, profile);
            _lockStore.Write(newLock);

            // NOTE: IAssetCatalog has no RefreshAsync — it auto-refreshes via FileSystemWatcher.

            var result = new BootstrapResult
            {
                Success = true,
                ActiveProfile = profile,
                ChangesApplied = true,
            };
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
            return new BootstrapResult
            {
                Success = false,
                ActiveProfile = options.PreselectedProfile ?? ProfileTier.TryItOut,
                ChangesApplied = false,
                ErrorMessage = ex.Message,
            };
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
            return await _ui.PickProfileAsync(ProfileChoiceCatalog.All, ct).ConfigureAwait(false);
        }

        return existingLock.SelectedProfile;
    }

    private static InstallStateLock BuildUpdatedLock(
        InstallManifest manifest,
        InstallStateLock existingLock,
        AssetPlan plan,
        ProfileTier profile
    )
    {
        var installed = existingLock.Assets.ToDictionary(
            kvp => kvp.Key,
            kvp => kvp.Value,
            StringComparer.Ordinal
        );

        foreach (var item in plan.Items)
        {
            installed[item.Entry.Id] = new InstalledAssetRecord(
                Version: item.Entry.Source.SourceVersion,
                Sha256: item.Entry.Sha256,
                VerifiedAt: DateTimeOffset.UtcNow
            );
        }

        return new InstallStateLock(
            SchemaVersion: 1,
            ManifestVersion: manifest.ManifestVersion,
            InstalledAt: DateTimeOffset.UtcNow,
            SelectedProfile: profile,
            Assets: installed,
            UserExclusions: existingLock.UserExclusions
        );
    }
}
