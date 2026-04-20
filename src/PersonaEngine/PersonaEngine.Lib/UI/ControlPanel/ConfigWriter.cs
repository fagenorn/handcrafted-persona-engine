using System.Collections.Concurrent;
using System.Collections.Frozen;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using PersonaEngine.Lib.Configuration;

namespace PersonaEngine.Lib.UI.ControlPanel;

public sealed class ConfigWriter : IConfigWriter
{
    // Enums are persisted as their string names so appsettings.json stays
    // human-editable and round-trips with Microsoft.Extensions.Configuration's
    // case-insensitive string→enum binding.
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly string _configFilePath;

    private readonly int _debounceMs;

    private readonly ILogger<ConfigWriter>? _logger;

    private readonly FrozenDictionary<Type, string> _sectionPaths;

    private readonly ConcurrentDictionary<string, JsonNode> _pendingSections = new();

    private readonly Lock _timerLock = new();

    private readonly SemaphoreSlim _flushLock = new(1, 1);

    private Timer? _debounceTimer;

    private volatile bool _disposed;

    public DateTime? LastSaveTime { get; private set; }

    public ConfigWriter(
        string configFilePath,
        int debounceMs = 300,
        ILogger<ConfigWriter>? logger = null
    )
    {
        _configFilePath = configFilePath ?? throw new ArgumentNullException(nameof(configFilePath));
        _debounceMs = debounceMs;
        _logger = logger;

        _sectionPaths = DiscoverPaths(typeof(AvatarAppConfig), "Config");
    }

    public void Write<T>(T options)
        where T : notnull
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var type = typeof(T);
        if (!_sectionPaths.TryGetValue(type, out var sectionPath))
        {
            throw new InvalidOperationException(
                $"Type '{type.FullName}' is not a registered options type in AvatarAppConfig. "
                    + "Only types reachable from AvatarAppConfig properties are supported."
            );
        }

        var node =
            JsonSerializer.SerializeToNode(options, SerializerOptions)
            ?? throw new InvalidOperationException($"Failed to serialize '{type.Name}' to JSON.");

        _pendingSections[sectionPath] = node;

        ResetDebounceTimer();
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;

        Timer? timer;
        lock (_timerLock)
        {
            timer = _debounceTimer;
            _debounceTimer = null;
        }

        // Timer.Dispose() alone does NOT wait for in-flight callbacks to
        // finish — if a callback is currently inside the FlushToDisk try
        // block, it still owns _flushLock and will call Release() later. If
        // we then Dispose() _flushLock below, the callback's Release fires
        // on a disposed semaphore and throws ObjectDisposedException on a
        // ThreadPool thread, which is an unhandled crash. Using the
        // Dispose(WaitHandle) overload blocks here until every pending
        // callback has actually returned.
        if (timer is not null)
        {
            using var done = new ManualResetEvent(false);
            if (timer.Dispose(done))
            {
                done.WaitOne();
            }
        }

        // At this point no Timer callback is running, so _flushLock is safe
        // to acquire, drain, and dispose.
        _flushLock.Wait();
        try
        {
            if (!_pendingSections.IsEmpty)
            {
                FlushToDisk();
            }
        }
        finally
        {
            _flushLock.Release();
            _flushLock.Dispose();
        }
    }

    private void ResetDebounceTimer()
    {
        lock (_timerLock)
        {
            if (_debounceTimer is null)
            {
                _debounceTimer = new Timer(
                    _ =>
                    {
                        if (!_flushLock.Wait(0))
                            return;

                        try
                        {
                            FlushToDisk();
                        }
                        finally
                        {
                            _flushLock.Release();
                        }
                    },
                    null,
                    _debounceMs,
                    Timeout.Infinite
                );
            }
            else
            {
                _debounceTimer.Change(_debounceMs, Timeout.Infinite);
            }
        }
    }

    private void FlushToDisk()
    {
        if (_pendingSections.IsEmpty)
            return;

        // Drain pending sections atomically
        var toWrite = new Dictionary<string, JsonNode>();
        foreach (var key in _pendingSections.Keys)
        {
            if (_pendingSections.TryRemove(key, out var node))
            {
                toWrite[key] = node;
            }
        }

        if (toWrite.Count == 0)
            return;

        try
        {
            var json = File.Exists(_configFilePath)
                ? JsonNode.Parse(File.ReadAllText(_configFilePath)) ?? new JsonObject()
                : new JsonObject();

            foreach (var (path, node) in toWrite)
            {
                SetSection(json, path, node);
            }

            File.WriteAllText(_configFilePath, json.ToJsonString(SerializerOptions));
            LastSaveTime = DateTime.UtcNow;

            _logger?.LogDebug(
                "ConfigWriter: flushed {Count} section(s) to {Path}",
                toWrite.Count,
                _configFilePath
            );
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(
                ex,
                "ConfigWriter: failed to flush config to {Path}; re-queuing {Count} section(s)",
                _configFilePath,
                toWrite.Count
            );

            // Re-queue failed sections so they are retried on next flush
            foreach (var (path, node) in toWrite)
            {
                // Only re-queue if there is not already a newer pending write for the same path
                _pendingSections.TryAdd(path, node);
            }

            // Do not reset the timer after disposal — that would create an orphaned timer
            if (!_disposed)
            {
                ResetDebounceTimer();
            }
        }
    }

    /// <summary>
    ///     Navigates the JSON tree by splitting the path on ':', creating intermediate
    ///     <see cref="JsonObject" />s as needed, then replaces the leaf with a deep clone of <paramref name="value" />.
    /// </summary>
    private static void SetSection(JsonNode root, string path, JsonNode value)
    {
        var segments = path.Split(':');
        var current = root;

        for (var i = 0; i < segments.Length - 1; i++)
        {
            var segment = segments[i];
            var obj = current.AsObject();

            if (obj[segment] is not JsonObject child)
            {
                child = [];
                obj[segment] = child;
            }

            current = child;
        }

        var leaf = segments[^1];
        current.AsObject()[leaf] = value.DeepClone();
    }

    /// <summary>
    ///     Reflects over <paramref name="rootType" />'s properties, building a mapping from
    ///     each discovered options type to its colon-delimited section path.
    ///     Skips primitives, strings, <see cref="TimeSpan" />, <see cref="DateTime" />,
    ///     arrays, generic collections, and non-PersonaEngine types.
    /// </summary>
    private static FrozenDictionary<Type, string> DiscoverPaths(Type rootType, string rootPath)
    {
        var result = new Dictionary<Type, string>();
        result.TryAdd(rootType, rootPath);
        Walk(rootType, rootPath, result, []);
        return result.ToFrozenDictionary();
    }

    private static void Walk(
        Type type,
        string path,
        Dictionary<Type, string> result,
        HashSet<Type> visited
    )
    {
        if (!visited.Add(type))
            return;

        foreach (var prop in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            var propType = prop.PropertyType;

            if (ShouldSkip(propType))
                continue;

            var childPath = $"{path}:{prop.Name}";

            // Map this type to its path (first encounter wins — avoids duplicate-type collisions)
            result.TryAdd(propType, childPath);

            // Recurse into the child type's own properties
            Walk(propType, childPath, result, visited);
        }

        visited.Remove(type);
    }

    private static bool ShouldSkip(Type type)
    {
        if (type.IsPrimitive || type.IsEnum || type == typeof(string) || type == typeof(decimal))
            return true;

        if (type == typeof(DateTime) || type == typeof(DateTimeOffset) || type == typeof(TimeSpan))
            return true;

        if (type.IsArray)
            return true;

        if (type.IsGenericType)
            return true;

        // Only recurse into PersonaEngine types
        var ns = type.Namespace;
        if (ns is null || !ns.StartsWith("PersonaEngine.", StringComparison.Ordinal))
            return true;

        return false;
    }
}
