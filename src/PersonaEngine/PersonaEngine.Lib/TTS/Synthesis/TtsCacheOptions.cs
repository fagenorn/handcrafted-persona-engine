namespace PersonaEngine.Lib.TTS.Synthesis;

/// <summary>
///     Options for configuring the TTS memory cache behavior
/// </summary>
public class TtsCacheOptions
{
    /// <summary>
    ///     Maximum number of items to store in the cache (0 = unlimited)
    /// </summary>
    public int MaxItems { get; set; } = 1000;

    /// <summary>
    ///     Time after which cache items expire (null = never expire)
    /// </summary>
    public TimeSpan? ItemExpiration { get; set; } = TimeSpan.FromHours(1);

    /// <summary>
    ///     Whether to collect performance metrics
    /// </summary>
    public bool CollectMetrics { get; set; } = true;
}
