using System.Collections.Frozen;

namespace PersonaEngine.Lib.Assets;

public sealed class AssetCatalog : IAssetCatalog, IDisposable
{
    private readonly FrozenDictionary<AssetId, AssetCatalogManifestEntry> _entries;
    private readonly FrozenSet<AssetId> _shippedDefaults;
    private readonly IReadOnlyDictionary<UserAssetType, string> _userContentRoots;
    private readonly List<UserContentWatcher> _watchers = new();

    private FrozenDictionary<FeatureId, bool> _featureCache;

    public event EventHandler<AssetCatalogChangedEventArgs>? Changed;

    public AssetCatalog(
        IReadOnlyList<AssetCatalogManifestEntry> entries,
        IReadOnlyDictionary<UserAssetType, string> userContentRoots,
        IReadOnlyList<AssetId> shippedDefaults
    )
    {
        _entries = entries.ToFrozenDictionary(e => e.Id);
        _userContentRoots = userContentRoots;
        _shippedDefaults = shippedDefaults.ToFrozenSet();
        _featureCache = ComputeFeatureCache();

        foreach (var (type, root) in userContentRoots)
        {
            var watcher = new UserContentWatcher(root, debounce: TimeSpan.FromMilliseconds(250));
            watcher.Changed += (_, _) =>
            {
                _featureCache = ComputeFeatureCache();
                Changed?.Invoke(this, new AssetCatalogChangedEventArgs(Array.Empty<AssetId>()));
            };
            _watchers.Add(watcher);
        }
    }

    public AssetState GetAssetState(AssetId id)
    {
        if (!_entries.TryGetValue(id, out var entry))
            return AssetState.Missing;

        if (File.Exists(entry.AbsoluteInstallPath))
            return AssetState.Available;
        if (
            Directory.Exists(entry.AbsoluteInstallPath)
            && Directory.EnumerateFileSystemEntries(entry.AbsoluteInstallPath).Any()
        )
            return AssetState.Available;

        return AssetState.Missing;
    }

    public bool IsFeatureEnabled(FeatureId feature) =>
        _featureCache.TryGetValue(feature, out var enabled) && enabled;

    public IReadOnlyList<UserAsset> GetUserAssets(UserAssetType type)
    {
        if (!_userContentRoots.TryGetValue(type, out var root))
            return Array.Empty<UserAsset>();
        if (!Directory.Exists(root))
            return Array.Empty<UserAsset>();

        var defaultPaths = _entries
            .Values.Where(e => e.UserAssetCategory == type && _shippedDefaults.Contains(e.Id))
            .Select(e =>
                Path.GetFullPath(e.AbsoluteInstallPath.TrimEnd(Path.DirectorySeparatorChar))
            )
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return Directory
            .EnumerateDirectories(root)
            .OrderBy(d => d, StringComparer.OrdinalIgnoreCase)
            .Select(d => new UserAsset(
                Type: type,
                DisplayName: Path.GetFileName(d.TrimEnd(Path.DirectorySeparatorChar)),
                AbsolutePath: d,
                IsDefault: defaultPaths.Contains(
                    Path.GetFullPath(d.TrimEnd(Path.DirectorySeparatorChar))
                )
            ))
            .ToArray();
    }

    private FrozenDictionary<FeatureId, bool> ComputeFeatureCache()
    {
        var byFeature = new Dictionary<FeatureId, List<AssetCatalogManifestEntry>>();
        foreach (var entry in _entries.Values)
        foreach (var gate in entry.Gates)
        {
            if (!byFeature.TryGetValue(gate, out var list))
                byFeature[gate] = list = new List<AssetCatalogManifestEntry>();
            list.Add(entry);
        }

        return byFeature.ToFrozenDictionary(
            kvp => kvp.Key,
            kvp => kvp.Value.All(e => GetAssetState(e.Id) == AssetState.Available)
        );
    }

    public void Dispose()
    {
        foreach (var w in _watchers)
            w.Dispose();
        _watchers.Clear();
    }
}
