namespace PersonaEngine.Lib.Utils.Pooling;

/// <summary>
///     Configuration for the ArrayPools used in the library.
/// </summary>
public class ArrayPoolConfig
{
    /// <summary>
    ///     Determines whether arrays should be cleared before being returned to the pool.
    /// </summary>
    /// <remarks>
    ///     Default value is <c>false</c> for performance reasons. If you are working with sensitive data, you may want to set
    ///     this to <c>true</c>.
    /// </remarks>
    public static bool ClearOnReturn { get; set; }
}
