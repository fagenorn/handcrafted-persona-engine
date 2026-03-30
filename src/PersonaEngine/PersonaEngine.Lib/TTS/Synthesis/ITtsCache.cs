namespace PersonaEngine.Lib.TTS.Synthesis;

/// <summary>
///     Interface for a TTS caching mechanism
/// </summary>
public interface ITtsCache : IAsyncDisposable
{
    /// <summary>
    ///     Gets or adds an item to the cache
    /// </summary>
    /// <typeparam name="T">The type of the item</typeparam>
    /// <param name="key">The cache key</param>
    /// <param name="valueFactory">Factory function to create the value if not found</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The cached or newly created value</returns>
    Task<T> GetOrAddAsync<T>(
        string key,
        Func<CancellationToken, Task<T>> valueFactory,
        CancellationToken cancellationToken = default
    )
        where T : class;

    /// <summary>
    ///     Removes an item from the cache
    /// </summary>
    /// <param name="key">The cache key to remove</param>
    void Remove(string key);

    /// <summary>
    ///     Clears all items from the cache
    /// </summary>
    void Clear();

    /// <summary>
    ///     Gets the current cache statistics
    /// </summary>
    /// <returns>Tuple with current stats</returns>
    (int Size, long Hits, long Misses, long Evictions) GetStatistics();
}
