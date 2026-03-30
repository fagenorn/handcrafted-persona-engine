using System.Collections.Concurrent;
using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace PersonaEngine.Lib.TTS.Synthesis;

/// <summary>
///     Enhanced memory-efficient TTS cache implementation with expiration and metrics
/// </summary>
public class TtsMemoryCache : ITtsCache, IAsyncDisposable
{
    private readonly ConcurrentDictionary<string, CacheItem> _cache = new();

    private readonly Timer _cleanupTimer;

    private readonly ILogger<TtsMemoryCache> _logger;

    private readonly TtsCacheOptions _options;

    private readonly ConcurrentDictionary<string, Stopwatch> _pendingOperations = new();

    private bool _disposed;

    private long _evictions;

    // Metrics
    private long _hits;

    private long _misses;

    public TtsMemoryCache(ILogger<TtsMemoryCache> logger, IOptions<TtsCacheOptions> options = null)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _options = options?.Value ?? new TtsCacheOptions();

        // Set up periodic cleanup if expiration is enabled
        if (_options.ItemExpiration.HasValue)
        {
            // Run cleanup at half the expiration interval or at least every minute
            var cleanupInterval = TimeSpan.FromMilliseconds(
                Math.Max(_options.ItemExpiration.Value.TotalMilliseconds / 2, 60000)
            );

            _cleanupTimer = new Timer(CleanupExpiredItems, null, cleanupInterval, cleanupInterval);
        }
    }

    /// <summary>
    ///     Gets or adds an item to the cache with metrics and expiration support
    /// </summary>
    public async Task<T> GetOrAddAsync<T>(
        string key,
        Func<CancellationToken, Task<T>> valueFactory,
        CancellationToken cancellationToken = default
    )
        where T : class
    {
        // Argument validation
        if (string.IsNullOrEmpty(key))
        {
            throw new ArgumentException("Cache key cannot be null or empty", nameof(key));
        }

        if (valueFactory == null)
        {
            throw new ArgumentNullException(nameof(valueFactory));
        }

        // Check for cancellation
        cancellationToken.ThrowIfCancellationRequested();

        // Check if the item exists in cache and is valid
        if (_cache.TryGetValue(key, out var cacheItem))
        {
            if (cacheItem.Value is T typedValue && !IsExpired(cacheItem))
            {
                Interlocked.Increment(ref _hits);
                _logger.LogDebug("Cache hit for key {Key}", key);

                return typedValue;
            }

            // Item exists but is either wrong type or expired - remove it
            _cache.TryRemove(key, out _);
        }

        // Item not in cache or was invalid, create it
        Interlocked.Increment(ref _misses);
        _logger.LogDebug("Cache miss for key {Key}, creating new value", key);

        var stopwatch = new Stopwatch();
        if (_options.CollectMetrics)
        {
            _pendingOperations[key] = stopwatch;
            stopwatch.Start();
        }

        try
        {
            // Create the value
            var value = await valueFactory(cancellationToken);

            if (value == null)
            {
                _logger.LogWarning("Value factory for key {Key} returned null", key);

                return null;
            }

            // Enforce maximum cache size if configured
            EnforceSizeLimit();

            // Add to cache using GetOrAdd to handle the case where another thread might have
            // added the same key while we were creating the value
            var newItem = new CacheItem { Value = value };
            var actualItem = _cache.GetOrAdd(key, newItem);

            // If another thread beat us to it and that value is of the correct type, use it
            if (
                actualItem != newItem
                && actualItem.Value is T existingValue
                && !IsExpired(actualItem)
            )
            {
                _logger.LogDebug("Another thread already added key {Key}", key);

                return existingValue;
            }

            return value;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Error creating value for key {Key}", key);

            throw;
        }
        finally
        {
            if (_options.CollectMetrics && _pendingOperations.TryRemove(key, out var sw))
            {
                sw.Stop();
                _logger.LogDebug(
                    "Value creation for key {Key} took {ElapsedMs}ms",
                    key,
                    sw.ElapsedMilliseconds
                );
            }
        }
    }

    /// <summary>
    ///     Removes an item from the cache
    /// </summary>
    public void Remove(string key)
    {
        if (string.IsNullOrEmpty(key))
        {
            throw new ArgumentException("Cache key cannot be null or empty", nameof(key));
        }

        if (_cache.TryRemove(key, out _))
        {
            _logger.LogDebug("Removed item with key {Key} from cache", key);
        }
    }

    /// <summary>
    ///     Clears the cache
    /// </summary>
    public void Clear()
    {
        _cache.Clear();
        _logger.LogInformation("Cache cleared");
    }

    /// <summary>
    ///     Disposes the cache resources
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        _cleanupTimer?.Dispose();
        _cache.Clear();

        var stats = GetStatistics();
        _logger.LogInformation(
            "Cache disposed. Final stats: Size={Size}, Hits={Hits}, Misses={Misses}, Evictions={Evictions}",
            stats.Size,
            stats.Hits,
            stats.Misses,
            stats.Evictions
        );

        await Task.CompletedTask;
    }

    /// <summary>
    ///     Gets the current cache statistics
    /// </summary>
    public (int Size, long Hits, long Misses, long Evictions) GetStatistics()
    {
        return (_cache.Count, _hits, _misses, _evictions);
    }

    /// <summary>
    ///     Determines if a cache item has expired
    /// </summary>
    private bool IsExpired(CacheItem item)
    {
        if (_options.ItemExpiration == null)
        {
            return false;
        }

        return DateTime.UtcNow - item.CreatedAt > _options.ItemExpiration.Value;
    }

    /// <summary>
    ///     Enforces the maximum cache size by removing oldest items if necessary
    /// </summary>
    private void EnforceSizeLimit()
    {
        if (_options.MaxItems <= 0 || _cache.Count < _options.MaxItems)
        {
            return;
        }

        // Get all items with their ages
        var items = _cache.ToArray();

        // Sort by creation time (oldest first)
        Array.Sort(items, (a, b) => a.Value.CreatedAt.CompareTo(b.Value.CreatedAt));

        // Remove oldest items to get back under the limit
        // We'll remove 10% of max capacity to avoid doing this too frequently
        var toRemove = Math.Max(1, _options.MaxItems / 10);

        for (var i = 0; i < toRemove && i < items.Length; i++)
        {
            if (_cache.TryRemove(items[i].Key, out _))
            {
                Interlocked.Increment(ref _evictions);
            }
        }

        _logger.LogInformation("Removed {Count} items from cache due to size limit", toRemove);
    }

    /// <summary>
    ///     Periodically removes expired items from the cache
    /// </summary>
    private void CleanupExpiredItems(object state)
    {
        if (_disposed)
        {
            return;
        }

        try
        {
            var removed = 0;
            foreach (var key in _cache.Keys)
            {
                if (_cache.TryGetValue(key, out var item) && IsExpired(item))
                {
                    if (_cache.TryRemove(key, out _))
                    {
                        removed++;
                        Interlocked.Increment(ref _evictions);
                    }
                }
            }

            if (removed > 0)
            {
                _logger.LogInformation("Removed {Count} expired items from cache", removed);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during cache cleanup");
        }
    }

    private class CacheItem
    {
        public object Value { get; set; }

        public DateTime CreatedAt { get; } = DateTime.UtcNow;
    }
}
