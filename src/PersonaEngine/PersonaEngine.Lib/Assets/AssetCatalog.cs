using System.Collections.Frozen;

namespace PersonaEngine.Lib.Assets;

public sealed class AssetCatalog : IAssetCatalog, IDisposable
{
    private readonly FrozenDictionary<AssetId, AssetCatalogManifestEntry> _entries;
    private readonly FrozenSet<AssetId> _shippedDefaults;
    private readonly IReadOnlyDictionary<UserAssetType, string> _userContentRoots;
    private readonly List<UserContentWatcher> _watchers = new();

    // A single snapshot holds both caches so watcher callbacks publish them
    // atomically (see <see cref="Volatile.Write"/> in <see cref="Rebuild"/>).
    // Without atomic publication, a reader could observe a fresh asset-state
    // cache against a stale feature cache and report a feature as missing
    // even though all its gated assets just landed on disk.
    private Snapshot _snapshot;

    public event EventHandler? Changed;

    public AssetCatalog(
        IReadOnlyList<AssetCatalogManifestEntry> entries,
        IReadOnlyDictionary<UserAssetType, string> userContentRoots,
        IReadOnlyList<AssetId> shippedDefaults
    )
    {
        _entries = entries.ToFrozenDictionary(e => e.Id);
        _userContentRoots = userContentRoots;
        _shippedDefaults = shippedDefaults.ToFrozenSet();
        _snapshot = BuildSnapshot();

        foreach (var (_, root) in userContentRoots)
        {
            var watcher = new UserContentWatcher(root, debounce: TimeSpan.FromMilliseconds(250));
            watcher.Changed += (_, _) =>
            {
                Volatile.Write(ref _snapshot, BuildSnapshot());
                Changed?.Invoke(this, EventArgs.Empty);
            };
            _watchers.Add(watcher);
        }
    }

    public AssetState GetAssetState(AssetId id) =>
        Volatile.Read(ref _snapshot).States.TryGetValue(id, out var state)
            ? state
            : AssetState.Missing;

    public bool IsFeatureEnabled(FeatureId feature) =>
        Volatile.Read(ref _snapshot).Features.TryGetValue(feature, out var enabled) && enabled;

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

    private Snapshot BuildSnapshot()
    {
        // Probe each asset's on-disk state once — callers hit this via UI render
        // paths, so we don't want to re-stat the filesystem on every call.
        var states = _entries.ToFrozenDictionary(kvp => kvp.Key, kvp => ProbeState(kvp.Value));

        var byFeature = new Dictionary<FeatureId, List<AssetId>>();
        foreach (var entry in _entries.Values)
        foreach (var gate in entry.Gates)
        {
            if (!byFeature.TryGetValue(gate, out var list))
                byFeature[gate] = list = new List<AssetId>();
            list.Add(entry.Id);
        }

        var features = byFeature.ToFrozenDictionary(
            kvp => kvp.Key,
            kvp => kvp.Value.All(id => states[id] == AssetState.Available)
        );

        return new Snapshot(states, features);
    }

    private static AssetState ProbeState(AssetCatalogManifestEntry entry)
    {
        if (File.Exists(entry.AbsoluteInstallPath))
            return AssetState.Available;
        if (
            Directory.Exists(entry.AbsoluteInstallPath)
            && Directory.EnumerateFileSystemEntries(entry.AbsoluteInstallPath).Any()
        )
            return AssetState.Available;

        return AssetState.Missing;
    }

    private sealed record Snapshot(
        FrozenDictionary<AssetId, AssetState> States,
        FrozenDictionary<FeatureId, bool> Features
    );

    public void Dispose()
    {
        foreach (var w in _watchers)
            w.Dispose();
        _watchers.Clear();
    }
}
